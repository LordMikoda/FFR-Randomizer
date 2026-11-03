-- FFR Randomizer for UE4SS.
-- Reads Randomizer.txt, builds a seeded plan from the game's own tables and
-- applies it in memory. Nothing on disk in the game changes, and removing this
-- folder undoes everything.

local MOD_VERSION = "0.4.0"
local LOG_PREFIX = "[FFRRandomizer] "
local CHECK_INTERVAL_MS = 3000

local function log(message)
    print(LOG_PREFIX .. message .. "\n")
end

local MOD_DIR = debug.getinfo(1, "S").source:match("^@(.*[\\/])Scripts[\\/]main%.lua$")
    or "ue4ss/Mods/FFRRandomizer/"
local SETTINGS_FILE = MOD_DIR .. "Randomizer.txt"

local hasNames, ITEM_NAMES = pcall(require, "ItemNames")
if not hasNames or type(ITEM_NAMES) ~= "table" then
    ITEM_NAMES = {}
end

local hasGroups, ENCOUNTER_GROUPS = pcall(require, "EncounterGroups")
if not hasGroups or type(ENCOUNTER_GROUPS) ~= "table" then
    ENCOUNTER_GROUPS = nil
end

local hasAbilities, VISION_ABILITIES = pcall(require, "VisionAbilities")
if not hasAbilities or type(VISION_ABILITIES) ~= "table" then
    VISION_ABILITIES = nil
end

local TABLES = {
    chests = "/Game/Datatable/Map/Gimmick/TreasureBox/CDT_MapGimmickTreasureBox.CDT_MapGimmickTreasureBox",
    units = "/Game/Datatable/Unit/DT_UnitParameter.DT_UnitParameter",
    shops = "/Game/Datatable/Shop/DT_ShopList.DT_ShopList",
    events = "/Game/Datatable/Event/TalkEvent/CDT_TalkEventData_Prod.CDT_TalkEventData_Prod",
    questBoard = "/Game/Datatable/Quest/CDT_QuestBoardData.CDT_QuestBoardData",
    battleGroups = "/Game/Datatable/Encount/DT_BattleGroup.DT_BattleGroup",
    unitDesign = "/Game/Datatable/Battle/Unit/DT_BtlUnitDesignParameter.DT_BtlUnitDesignParameter",
    visionItems = "/Game/Datatable/Item/Vision/DT_VisionItemData.DT_VisionItemData",
    awakening = "/Game/Datatable/Item/Vision/DT_VisionAwakeningMasteryData.DT_VisionAwakeningMasteryData",
    synchro = "/Game/Datatable/Item/Vision/DT_VisionSynchroMasteryData.DT_VisionSynchroMasteryData",
    skills = "/Game/Datatable/Skill/DT_SkillData.DT_SkillData",
    passives = "/Game/Datatable/Skill/DT_PassiveSkillData.DT_PassiveSkillData",
}
-- Tables the plan can do without, the ones only the enemy and boss shuffles
-- need, and the ones only the Vision ability shuffle needs.
local OPTIONAL_TABLES = { questBoard = true }
local ENEMY_TABLES = { battleGroups = true, unitDesign = true }
local ABILITY_TABLES = { awakening = true, synchro = true, skills = true, passives = true }

-- Only Consumable, Equipment and Seed items move. Valuables, Artifacts and
-- Visions are key items or have their own rules, so they never leave their spot.
local ITEM_TABLES = {
    { kind = "Consumable", path = "/Game/Datatable/Item/DT_ConsumablesItemData.DT_ConsumablesItemData" },
    { kind = "Equipment", path = "/Game/Datatable/Item/DT_EquipmentItemData.DT_EquipmentItemData" },
    { kind = "Seed", path = "/Game/Datatable/Item/DT_SeedItemData.DT_SeedItemData" },
    { kind = "Valuable", path = "/Game/Datatable/Item/DT_ValuableItemData.DT_ValuableItemData" },
    { kind = "Artifact", path = "/Game/Datatable/Item/DT_ArtifactItemData.DT_ArtifactItemData" },
    { kind = "Vision", path = "/Game/Datatable/Item/Vision/DT_VisionItemData.DT_VisionItemData" },
}
local SHUFFLE_KINDS = { Consumable = true, Equipment = true, Seed = true }

-- eUnitCategory: Enemy = 2, Boss = 3. Playable, Vision, Beast and Test units keep their items.
local DROP_CATEGORIES = { [2] = true, [3] = true }
local UNIT_ITEM_FIELDS = { "DropNormalItem", "DropRareItem", "StealNormalItem", "StealRareItem" }
local EQUIPMENT_TYPES = { [1] = "Weapon", [2] = "Armor", [3] = "Accessory" }

-- What a swapped-in enemy takes over from the enemy it replaces, so a spot
-- keeps its difficulty and rewards. Abilities, looks and resistances stay
-- with the enemy itself.
local ENEMY_SPOT_FIELDS = {
    "Level", "LevelParamId", "HitPoint", "MagicPoint", "Breakpoint",
    "MaxHitPoint", "MaxMagicPoint", "MaxBreakPoint",
    "Attack", "Defence", "Intelligence", "Mind", "Divine", "Agility",
    "Experience", "Money",
    "DropNormalItem", "DropNormalItemRate", "DropRareItem", "DropRareItemRate",
    "StealNormalItem", "StealRareItem", "StealItemRate",
}
local ENEMY_CATEGORY = 2
local BOSS_CATEGORY = 3

-- Boss fights that never swap, on top of the rules in shuffleBosses. The
-- Twelve Arms fights run in chained phases, and Leviathan's and the Antlion's
-- battle events are named for the monster, so they likely drive its mechanics.
local function isFixedBossGroup(id)
    return (id >= 9922 and id <= 9999) or id == 9021 or id == 9060
end

-- Developer test battles (groups 1 to 6, such as "1-enemy check", and the
-- 300000 parts test) never run in play. They use Steel Bats and Goblins, so
-- counting them as scripted fights would keep those enemies out of the shuffle.
local function isDebugGroup(id)
    return id < 100 or id >= 300000
end
local UNIT_SIZES = { [1] = "S", [2] = "M", [3] = "L", [4] = "LL", [5] = "XL" }

local VISION_COUNT = 26

-- Leah, Ayaka and Y'shtola. With supports on, each keeps at least
-- SUPPORT_MIN_HEALING abilities from HEALING_SKILLS.
local SUPPORT_VISIONS = { [13045] = true, [13051] = true, [13127] = true }
local SUPPORT_MIN_HEALING = 5
-- Abilities that heal, revive or cure allies. The skill table can't tell
-- these apart from buffs, so the list is by hand.
local HEALING_SKILLS = {
    [210010] = true, [210020] = true, [210030] = true, -- Cure, Cura, Curaga
    [210050] = true, [414510] = true,                  -- Regen, Hi Regen
    [210120] = true, [210130] = true,                  -- Esuna, Esunaga
    [210140] = true, [210150] = true, [210160] = true, -- Raise, Arise, Reraise
    [220410] = true,                                   -- Manaward
    [220480] = true, [220490] = true, [220500] = true, -- Cure I, II and III of the Seventh Dawn
    [400540] = true, [400550] = true,                  -- Recover, First Aid
    [400740] = true, [414500] = true,                  -- Prepare Healing, Purify
}

local CHEST_CAPS = { default = false, ["9"] = 9, ["50"] = 50 }

-- Settings ------------------------------------------------------------------

local SETTINGS_TEMPLATE = [[
# FFR Randomizer settings, written by the FFR Randomizer GUI. You can also edit it by hand.
# Keep the same file for a whole playthrough. Change it, then restart the game.
seed=%s
# on or off
items=%s
shops=%s
visions=%s
enemies=%s
bosses=%s
# Vision abilities. mix_physical_magic and supports only matter with abilities on.
abilities=%s
mix_physical_magic=%s
finishers=%s
supports=%s
# default, 9 or 50. Weapons, armor and accessories always stay at 1.
chest_quantity=%s
]]

local function trim(text)
    return (text:gsub("^%s+", ""):gsub("%s+$", ""))
end

local function writeSettings(settings)
    local file = io.open(SETTINGS_FILE, "w")
    if file == nil then
        log("Could not write " .. SETTINGS_FILE)
        return
    end
    local function onOff(value) return value and "on" or "off" end
    file:write(string.format(SETTINGS_TEMPLATE, settings.seed, onOff(settings.items),
        onOff(settings.shops), onOff(settings.visions), onOff(settings.enemies), onOff(settings.bosses),
        onOff(settings.abilities), onOff(settings.mixAbilities), onOff(settings.finishers),
        onOff(settings.supports), settings.chestQuantity))
    file:close()
end

local function readSettings()
    local values = {}
    local file = io.open(SETTINGS_FILE, "r")
    if file ~= nil then
        for line in file:lines() do
            local key, value = line:match("^%s*([%w_]+)%s*=(.*)$")
            if key ~= nil then
                values[key:lower()] = trim(value)
            end
        end
        file:close()
    end

    local function flag(key, default)
        local value = (values[key] or default):lower()
        return value ~= "off" and value ~= "false" and value ~= "0"
    end

    -- Everything after visions came later, so a file without the line keeps it off.
    local settings = {
        seed = values.seed or "",
        items = flag("items", "on"),
        shops = flag("shops", "on"),
        visions = flag("visions", "on"),
        enemies = flag("enemies", "off"),
        bosses = flag("bosses", "off"),
        abilities = flag("abilities", "off"),
        mixAbilities = flag("mix_physical_magic", "off"),
        finishers = flag("finishers", "off"),
        supports = flag("supports", "off"),
        chestQuantity = (values.chest_quantity or "default"):lower(),
    }
    if CHEST_CAPS[settings.chestQuantity] == nil then
        log("Unknown chest_quantity '" .. settings.chestQuantity .. "', using default.")
        settings.chestQuantity = "default"
    end
    -- A blank seed gets a new one, saved straight away so every launch of this
    -- playthrough keeps the same shuffle.
    if settings.seed == "" then
        settings.seed = tostring(os.time()) .. tostring(math.floor(os.clock() * 1000000) % 1000)
        writeSettings(settings)
        log("No seed set. Saved new seed " .. settings.seed .. " to Randomizer.txt")
    elseif file == nil then
        writeSettings(settings)
    end
    return settings
end

-- Seeded random numbers -----------------------------------------------------
-- FNV-1a turns the text seed into a number, then SplitMix64 makes the stream.
-- Lua 5.4 integers wrap at 64 bits, which both rely on.

local function hashText(text)
    local hash = -3750763034362895579 -- 0xcbf29ce484222325
    for i = 1, #text do
        hash = (hash ~ text:byte(i)) * 1099511628211
    end
    return hash
end

local function newRng(seed, stream)
    local state = hashText(seed .. ":" .. stream)
    local rng = {}
    function rng.next()
        state = state + -7046029254386353131 -- 0x9e3779b97f4a7c15
        local z = state
        z = (z ~ (z >> 30)) * -4658895280553007687 -- 0xbf58476d1ce4e5b9
        z = (z ~ (z >> 27)) * -7723592293110705685 -- 0x94d049bb133111eb
        return z ~ (z >> 31)
    end
    -- Returns 1..n.
    function rng.int(n)
        return ((rng.next() >> 1) % n) + 1
    end
    function rng.shuffle(list)
        for i = #list, 2, -1 do
            local j = rng.int(i)
            list[i], list[j] = list[j], list[i]
        end
    end
    return rng
end

-- Table access --------------------------------------------------------------

local function findTable(path)
    local dataTable = StaticFindObject(path)
    if dataTable ~= nil and dataTable:IsValid() then
        return dataTable
    end
    return nil
end

local function nameText(name)
    if type(name) == "string" then
        return name
    end
    return name:ToString()
end

-- Row names in byte order, so the plan never depends on the game's row order.
local function sortedRowNames(dataTable)
    local names = {}
    for _, name in ipairs(dataTable:GetRowNames()) do
        table.insert(names, nameText(name))
    end
    table.sort(names)
    return names
end

local function arrayItems(array)
    local items = {}
    if array == nil then
        return items
    end
    for i = 1, #array do
        items[i] = array[i]
    end
    return items
end

local function isTrue(value)
    return value == true or value == 1
end

-- A composite table keeps its own copy of rows, so its parent tables get the
-- same patch. Parents can be composite too.
local function tableFamily(root)
    local family, seen = {}, {}
    local function visit(dataTable)
        if dataTable == nil or not dataTable:IsValid() then
            return
        end
        local key = dataTable:GetFullName()
        if seen[key] then
            return
        end
        seen[key] = true
        table.insert(family, dataTable)
        -- Plain tables have no ParentTables, and UE4SS hands back an object
        -- that errors on use, so the whole read is guarded.
        local ok, parents = pcall(function() return arrayItems(dataTable.ParentTables) end)
        if ok then
            for _, parent in ipairs(parents) do
                visit(parent)
            end
        end
    end
    visit(root)
    return family
end

-- Item catalog --------------------------------------------------------------

local function itemName(id)
    return ITEM_NAMES[id] or ("Item " .. tostring(id))
end

local function buildCatalog()
    local catalog = {}
    for _, source in ipairs(ITEM_TABLES) do
        local dataTable = findTable(source.path)
        if dataTable == nil then
            return nil, source.path
        end
        dataTable:ForEachRow(function(_, row)
            local info = {
                kind = source.kind,
                sellable = source.kind ~= "Vision" and isTrue(row.isAvailableSale),
                price = row.buyingPrice or 0,
            }
            if source.kind == "Equipment" then
                info.equipmentType = row.EquipmentType
            end
            catalog[row.ID] = info
        end)
    end
    return catalog
end

-- Items that events take from the player, or that delivery quests ask for.
-- They stay where the game puts them so every quest keeps its source.
local function buildConsumed(events, questBoard)
    local consumed = {}
    events:ForEachRow(function(_, row)
        for _, entry in ipairs(arrayItems(row.ConsumeItemList)) do
            if entry.ID > 0 then
                consumed[entry.ID] = true
            end
        end
    end)
    if questBoard ~= nil then
        questBoard:ForEachRow(function(_, row)
            for _, entry in ipairs(arrayItems(row.ClearConditionList)) do
                if entry.ID > 0 then
                    consumed[entry.ID] = true
                end
            end
        end)
    else
        log("Quest board table not found. Delivery items are not protected.")
    end
    return consumed
end

-- Plan ----------------------------------------------------------------------
-- A patch sets one field: row[field], row[array][index][field], or, with no
-- field, the plain value row[array][index]. With sub as well, it sets
-- row[array][index][field][sub]. A later patch to the same spot replaces the
-- earlier one, so two passes never fight over a field.

local function newPlan()
    return { patches = {}, patchIndex = {}, spoiler = {}, unitItems = {} }
end

local function addPatch(plan, family, row, field, value, array, index, sub)
    local key = table.concat({ family, row, tostring(array), tostring(index), tostring(field), tostring(sub) }, "|")
    local existing = plan.patchIndex[key]
    if existing ~= nil then
        existing.value = value
        return
    end
    local patch = { family = family, row = row, field = field, value = value, array = array, index = index, sub = sub }
    plan.patchIndex[key] = patch
    table.insert(plan.patches, patch)
end

local function addSpoiler(plan, section, line)
    local lines = plan.spoiler[section]
    if lines == nil then
        lines = {}
        plan.spoiler[section] = lines
    end
    table.insert(lines, line)
end

local function shufflePool(plan, settings, tables, catalog, consumed)
    local function canMove(id)
        local info = catalog[id]
        return info ~= nil and SHUFFLE_KINDS[info.kind] and not consumed[id]
    end
    local function isGear(id)
        return catalog[id] ~= nil and catalog[id].kind == "Equipment"
    end

    local slots = {}
    for _, name in ipairs(sortedRowNames(tables.chests)) do
        local row = tables.chests:FindRow(name)
        if row.ItemId > 0 and canMove(row.ItemId) then
            table.insert(slots, { family = "chests", row = name, field = "ItemId",
                original = row.ItemId, count = row.ItemNum })
        end
    end
    for _, name in ipairs(sortedRowNames(tables.units)) do
        local row = tables.units:FindRow(name)
        if DROP_CATEGORIES[row.Category] then
            for _, field in ipairs(UNIT_ITEM_FIELDS) do
                if row[field] > 0 and canMove(row[field]) then
                    table.insert(slots, { family = "units", row = name, field = field,
                        original = row[field], unitId = row.ID })
                end
            end
        end
    end

    local items = {}
    for i, slot in ipairs(slots) do
        items[i] = slot.original
    end
    if settings.items then
        newRng(settings.seed, "items").shuffle(items)
    end

    local cap = CHEST_CAPS[settings.chestQuantity]
    local quantityRng = newRng(settings.seed, "quantity")
    local moved = 0
    for i, slot in ipairs(slots) do
        local item = items[i]
        if item ~= slot.original then
            addPatch(plan, slot.family, slot.row, slot.field, item)
            moved = moved + 1
        end
        if slot.family == "units" then
            plan.unitItems[slot.row] = plan.unitItems[slot.row] or {}
            plan.unitItems[slot.row][slot.field] = item
        end
        if slot.family == "chests" then
            local count
            if isGear(item) then
                count = 1
            elseif cap then
                count = quantityRng.int(cap)
            else
                count = math.max(slot.count, 1)
            end
            if count ~= slot.count then
                addPatch(plan, "chests", slot.row, "ItemNum", count)
            end
            addSpoiler(plan, "Chests", string.format("%-22s %s x%d   (was %s x%d)", slot.row,
                itemName(item), count, itemName(slot.original), slot.count))
        elseif item ~= slot.original then
            addSpoiler(plan, "Drops and steals", string.format("Unit %d %s, %s: %s   (was %s)",
                slot.unitId, slot.row, slot.field, itemName(item), itemName(slot.original)))
        end
    end
    log(string.format("Item pool: %d slots, %d moved, chest quantity %s.", #slots, moved,
        settings.chestQuantity))
end

local function shopCategory(info)
    if info.kind == "Equipment" then
        return EQUIPMENT_TYPES[info.equipmentType] or ("Equipment " .. tostring(info.equipmentType))
    end
    return info.kind
end

-- Each slot keeps its category, unlock condition, order limit and price ratio.
-- Slots selling an item that can't be sold, or that a quest takes, stay as they are.
local function shuffleShops(plan, settings, tables, catalog, consumed)
    local function canSell(id)
        local info = catalog[id]
        return info ~= nil and SHUFFLE_KINDS[info.kind] and info.sellable and info.price > 0
            and not consumed[id]
    end

    local pools = {}
    local ids = {}
    for id in pairs(catalog) do
        table.insert(ids, id)
    end
    table.sort(ids)
    for _, id in ipairs(ids) do
        if canSell(id) then
            local category = shopCategory(catalog[id])
            pools[category] = pools[category] or {}
            table.insert(pools[category], id)
        end
    end

    local rng = newRng(settings.seed, "shops")
    local changed = 0
    for _, name in ipairs(sortedRowNames(tables.shops)) do
        local row = tables.shops:FindRow(name)
        local slots = arrayItems(row.ItemList)
        local inShop, result = {}, {}
        for i, slot in ipairs(slots) do
            result[i] = slot.ItemId
            if not canSell(slot.ItemId) then
                inShop[slot.ItemId] = true
            end
        end
        for i, slot in ipairs(slots) do
            if canSell(slot.ItemId) then
                local pool = { table.unpack(pools[shopCategory(catalog[slot.ItemId])]) }
                rng.shuffle(pool)
                local pick = slot.ItemId
                for _, id in ipairs(pool) do
                    if not inShop[id] then
                        pick = id
                        break
                    end
                end
                inShop[pick] = true
                result[i] = pick
                if pick ~= slot.ItemId then
                    addPatch(plan, "shops", name, "ItemId", pick, "ItemList", i)
                    changed = changed + 1
                end
            end
        end
        local names = {}
        for _, id in ipairs(result) do
            if id > 0 then
                local info = catalog[id]
                table.insert(names, string.format("%s (%d G)", itemName(id), info and info.price or 0))
            end
        end
        if #names > 0 then
            addSpoiler(plan, "Shops", string.format("Shop %d %s: %s", row.ShopID, name,
                table.concat(names, ", ")))
        end
    end
    log(string.format("Shops: %d slots changed.", changed))
end

-- Rows ending in _Kari are early drafts that give the same Vision as a real
-- event. If one still runs, it follows the real event's new Vision.
local function shuffleVisions(plan, settings, tables, catalog)
    local slots, drafts = {}, {}
    for _, name in ipairs(sortedRowNames(tables.events)) do
        if name ~= "VisionGetTemplate" then
            local row = tables.events:FindRow(name)
            for i, entry in ipairs(arrayItems(row.ObtainItemList)) do
                local info = catalog[entry.ID]
                if info ~= nil and info.kind == "Vision" then
                    local slot = { row = name, index = i, original = entry.ID }
                    if name:match("_Kari$") then
                        table.insert(drafts, slot)
                    else
                        table.insert(slots, slot)
                    end
                end
            end
        end
    end
    if #slots ~= VISION_COUNT then
        log(string.format("Expected %d Vision rewards but found %d. Visions not shuffled.",
            VISION_COUNT, #slots))
        return
    end

    local visions = {}
    for i, slot in ipairs(slots) do
        visions[i] = slot.original
    end
    newRng(settings.seed, "visions").shuffle(visions)

    local replacement = {}
    for i, slot in ipairs(slots) do
        replacement[slot.original] = visions[i]
        if visions[i] ~= slot.original then
            addPatch(plan, "events", slot.row, "ID", visions[i], "ObtainItemList", slot.index)
        end
        addSpoiler(plan, "Visions", string.format("%-26s %s   (was %s)", slot.row,
            itemName(visions[i]), itemName(slot.original)))
    end
    for _, slot in ipairs(drafts) do
        local vision = replacement[slot.original]
        if vision ~= nil and vision ~= slot.original then
            addPatch(plan, "events", slot.row, "ID", vision, "ObtainItemList", slot.index)
        end
    end
    log(string.format("Visions: %d rewards shuffled.", #slots))
end

-- The first unit row for each unit ID, its category and its size.
local function readUnits(tables)
    local rowOf, category = {}, {}
    for _, name in ipairs(sortedRowNames(tables.units)) do
        local row = tables.units:FindRow(name)
        if rowOf[row.ID] == nil then
            rowOf[row.ID] = name
            category[row.ID] = row.Category
        end
    end
    local sizeOf = {}
    tables.unitDesign:ForEachRow(function(_, row)
        sizeOf[row.ID] = row.Size
    end)
    return rowOf, category, sizeOf
end

-- The swapped-in unit's row takes the replaced unit's spot fields. Drops and
-- steals come from the item shuffle when it moved them.
local function moveSpot(plan, tables, rowOf, id, new)
    local source = tables.units:FindRow(rowOf[id])
    local items = plan.unitItems[rowOf[id]] or {}
    for _, field in ipairs(ENEMY_SPOT_FIELDS) do
        local value = items[field]
        if value == nil then
            value = source[field]
        end
        addPatch(plan, "units", rowOf[new], field, value)
    end
end

-- One-for-one swap of random encounter enemies, within each size class. If A
-- replaces B, every B in a random encounter becomes A, and A's row takes B's
-- stats, rewards, drops and steals, so the spot plays and pays as before.
-- Enemies that also fight in scripted battles stay out, because those
-- battles would otherwise get the moved stats.
local function shuffleEnemies(plan, settings, tables)
    if ENCOUNTER_GROUPS == nil then
        log("EncounterGroups.lua is missing. Enemies not shuffled.")
        return
    end

    local rowOf, category, sizeOf = readUnits(tables)

    local groups, inEncounters, inOtherFights = {}, {}, {}
    for _, name in ipairs(sortedRowNames(tables.battleGroups)) do
        local row = tables.battleGroups:FindRow(name)
        if not isDebugGroup(row.ID) then
            local ids = arrayItems(row.UnitIdList)
            local random = ENCOUNTER_GROUPS[row.ID] == true
            for _, id in ipairs(ids) do
                if random then
                    inEncounters[id] = true
                else
                    inOtherFights[id] = true
                end
            end
            if random then
                table.insert(groups, { row = name, ids = ids })
            end
        end
    end

    local buckets, bucketNames = {}, {}
    local ids = {}
    for id in pairs(inEncounters) do
        if not inOtherFights[id] and rowOf[id] ~= nil and category[id] == ENEMY_CATEGORY then
            table.insert(ids, id)
        end
    end
    table.sort(ids)
    for _, id in ipairs(ids) do
        local size = UNIT_SIZES[sizeOf[id]] or "Unknown"
        if buckets[size] == nil then
            buckets[size] = {}
            table.insert(bucketNames, size)
        end
        table.insert(buckets[size], id)
    end

    local rng = newRng(settings.seed, "enemies")
    local replacement = {}
    table.sort(bucketNames)
    for _, size in ipairs(bucketNames) do
        local from = buckets[size]
        local to = { table.unpack(from) }
        rng.shuffle(to)
        for i, id in ipairs(from) do
            replacement[id] = to[i]
        end
    end

    for _, group in ipairs(groups) do
        for i, id in ipairs(group.ids) do
            local new = replacement[id]
            if new ~= nil and new ~= id then
                addPatch(plan, "battleGroups", group.row, nil, new, "UnitIdList", i)
            end
        end
    end

    local moved = 0
    for _, id in ipairs(ids) do
        local new = replacement[id]
        if new ~= id then
            moved = moved + 1
            moveSpot(plan, tables, rowOf, id, new)
        end
        addSpoiler(plan, "Enemies", string.format("Unit %d %s fights in place of unit %d %s (size %s)",
            new, rowOf[new], id, rowOf[id], UNIT_SIZES[sizeOf[id]] or "Unknown"))
    end
    log(string.format("Enemies: %d in random encounters, %d swapped.", #ids, moved))
end

-- An FName field that names something. Empty FNames read as "None".
local function hasName(value)
    if value == nil then
        return false
    end
    local text = nameText(value)
    return text ~= "" and text ~= "None"
end

-- One-for-one swap of bosses across boss fights, the same way as enemies: the
-- new boss keeps its own abilities, looks and resistances, and takes the
-- replaced boss's level, stats, EXP, Gil, drops and steals. Sizes aren't
-- matched, since most size classes hold only a few bosses.
-- A fight swaps only when it has exactly one boss, isn't a lose-on-purpose or
-- scripted-result fight, has a finish event (later phases of a chained fight
-- have none) and isn't on the fixed list. A boss that also appears in any
-- other battle stays out, so that battle keeps its stats.
local function shuffleBosses(plan, settings, tables)
    local rowOf, category, sizeOf = readUnits(tables)

    local fights, inFights, elsewhere = {}, {}, {}
    for _, name in ipairs(sortedRowNames(tables.battleGroups)) do
        local row = tables.battleGroups:FindRow(name)
        if not isDebugGroup(row.ID) then
            local ids = arrayItems(row.UnitIdList)
            local bossSlots = {}
            for i, id in ipairs(ids) do
                if category[id] == BOSS_CATEGORY then
                    table.insert(bossSlots, i)
                end
            end
            local usable = #bossSlots == 1
                and not (ENCOUNTER_GROUPS ~= nil and ENCOUNTER_GROUPS[row.ID])
                and not isTrue(row.isResultSkipOnLose) and not isTrue(row.isResultSkipOnWin)
                and hasName(row.battleFinishEventId)
                and not isFixedBossGroup(row.ID)
            for i, id in ipairs(ids) do
                if usable and i == bossSlots[1] then
                    inFights[id] = true
                else
                    elsewhere[id] = true
                end
            end
            if usable then
                table.insert(fights, { row = name, slot = bossSlots[1], id = ids[bossSlots[1]] })
            end
        end
    end

    local ids = {}
    for id in pairs(inFights) do
        if not elsewhere[id] and rowOf[id] ~= nil then
            table.insert(ids, id)
        end
    end
    table.sort(ids)

    local rng = newRng(settings.seed, "bosses")
    local to = { table.unpack(ids) }
    rng.shuffle(to)
    local replacement = {}
    for i, id in ipairs(ids) do
        replacement[id] = to[i]
    end

    for _, fight in ipairs(fights) do
        local new = replacement[fight.id]
        if new ~= nil and new ~= fight.id then
            addPatch(plan, "battleGroups", fight.row, nil, new, "UnitIdList", fight.slot)
        end
    end

    local moved = 0
    for _, id in ipairs(ids) do
        local new = replacement[id]
        if new ~= id then
            moved = moved + 1
            moveSpot(plan, tables, rowOf, id, new)
        end
        addSpoiler(plan, "Bosses", string.format("Unit %d %s fights in place of unit %d %s (size %s to %s)",
            new, rowOf[new], id, rowOf[id], UNIT_SIZES[sizeOf[new]] or "Unknown", UNIT_SIZES[sizeOf[id]] or "Unknown"))
    end
    log(string.format("Bosses: %d in boss fights, %d swapped.", #ids, moved))
end

-- Vision abilities ----------------------------------------------------------

local function abilityName(id)
    local names = VISION_ABILITIES and VISION_ABILITIES.names or {}
    return names[id] or ("Ability " .. tostring(id))
end

local function idSet(dataTable)
    local ids = {}
    dataTable:ForEachRow(function(_, row)
        ids[row.ID] = true
    end)
    return ids
end

-- The ActiveSkill and PassiveSkill slots of every awakening and synchro row.
-- parameterType reads as a bare number, so a slot's kind comes from the skill
-- table its ID is in, and a type number counts only when all of its slots
-- agree. BaseParameter and MasterSkill slots never qualify and stay put.
local function readAbilitySlots(tables)
    local skills, passives = idSet(tables.skills), idSet(tables.passives)
    local found, kindsOfType = {}, {}
    for _, family in ipairs({ "awakening", "synchro" }) do
        for _, name in ipairs(sortedRowNames(tables[family])) do
            local row = tables[family]:FindRow(name)
            for i, entry in ipairs(arrayItems(row.detailData)) do
                local id = arrayItems(entry.params)[1]
                local kind = (skills[id] and "active") or (passives[id] and "passive") or "other"
                local kinds = kindsOfType[entry.parameterType] or {}
                kindsOfType[entry.parameterType] = kinds
                kinds[kind] = true
                table.insert(found, { family = family, row = name, index = i, vision = row.ID,
                    type = entry.parameterType, kind = kind, original = id, value = id })
            end
        end
    end
    local slots = {}
    for _, slot in ipairs(found) do
        local kinds = kindsOfType[slot.type]
        if slot.kind ~= "other" and not kinds.other and not (kinds.active and kinds.passive) then
            table.insert(slots, slot)
        end
    end
    return slots
end

-- Two slots clash when they hold the same ability, or two passives from one
-- stat family such as Speed +10% and Speed +20%.
local function abilityKey(slot, id)
    if slot.kind == "passive" then
        return "p" .. (VISION_ABILITIES.families[id] or tostring(id))
    end
    return "a" .. tostring(id)
end

local function holdAbility(held, slot, change)
    local key = abilityKey(slot, slot.value)
    held[slot.vision][key] = (held[slot.vision][key] or 0) + change
end

-- Whether swapping a's and b's abilities leaves neither Vision with a clash.
local function canSwap(held, a, b)
    if a.vision == b.vision then
        return false
    end
    local keyA, keyB = abilityKey(a, a.value), abilityKey(b, b.value)
    return keyA ~= keyB and (held[a.vision][keyB] or 0) == 0 and (held[b.vision][keyA] or 0) == 0
end

local function swapAbilities(held, a, b)
    holdAbility(held, a, -1)
    holdAbility(held, b, -1)
    a.value, b.value = b.value, a.value
    holdAbility(held, a, 1)
    holdAbility(held, b, 1)
end

-- The first slot in the pool, from a random start, that b can swap with.
local function findSwap(pool, rng, test)
    local start = rng.int(#pool)
    for step = 0, #pool - 1 do
        local slot = pool[(start + step - 1) % #pool + 1]
        if test(slot) then
            return slot
        end
    end
    return nil
end

local function healingCount(pool, vision)
    local count = 0
    for _, slot in ipairs(pool) do
        if slot.vision == vision and HEALING_SKILLS[slot.value] then
            count = count + 1
        end
    end
    return count
end

-- Abilities swap between slots of the same kind, so each Vision keeps its
-- number of actives and passives and when it learns them. Physical and Magic
-- Visions shuffle apart unless mixing is on. A repair pass then clears any
-- Vision holding one ability twice, or two passives from one stat family.
-- The same ability on two different Visions is fine, as in the base game.
local function shuffleVisionAbilities(plan, settings, tables)
    if VISION_ABILITIES == nil then
        log("VisionAbilities.lua is missing. Vision abilities not shuffled.")
        return
    end
    local slots = readAbilitySlots(tables)

    local pools, poolNames, held = {}, {}, {}
    for _, slot in ipairs(slots) do
        local group = settings.mixAbilities and "All"
            or (VISION_ABILITIES.physical[slot.vision] and "Physical" or "Magic")
        local name = group .. " " .. slot.kind
        if pools[name] == nil then
            pools[name] = {}
            table.insert(poolNames, name)
        end
        table.insert(pools[name], slot)
        held[slot.vision] = held[slot.vision] or {}
    end
    table.sort(poolNames)

    local rng = newRng(settings.seed, "abilities")
    for _, name in ipairs(poolNames) do
        local pool = pools[name]
        local values = {}
        for i, slot in ipairs(pool) do
            values[i] = slot.original
        end
        rng.shuffle(values)
        for i, slot in ipairs(pool) do
            slot.value = values[i]
        end
    end
    for _, slot in ipairs(slots) do
        holdAbility(held, slot, 1)
    end

    local clashes = 0
    for _, name in ipairs(poolNames) do
        local pool = pools[name]
        for _, slot in ipairs(pool) do
            while held[slot.vision][abilityKey(slot, slot.value)] > 1 do
                local other = findSwap(pool, rng, function(other) return canSwap(held, slot, other) end)
                if other == nil then
                    clashes = clashes + 1
                    break
                end
                swapAbilities(held, slot, other)
            end
        end
    end

    -- Supports take healing from other Visions in their own pool, never from
    -- another support, giving back one of their other abilities in exchange.
    if settings.supports then
        local supports = {}
        for vision in pairs(SUPPORT_VISIONS) do
            if held[vision] ~= nil then
                table.insert(supports, vision)
            end
        end
        table.sort(supports)
        for _, vision in ipairs(supports) do
            local group = settings.mixAbilities and "All"
                or (VISION_ABILITIES.physical[vision] and "Physical" or "Magic")
            local pool = pools[group .. " active"] or {}
            while healingCount(pool, vision) < SUPPORT_MIN_HEALING do
                local given, taken
                for _, slot in ipairs(pool) do
                    if slot.vision == vision and not HEALING_SKILLS[slot.value] then
                        taken = findSwap(pool, rng, function(other)
                            return HEALING_SKILLS[other.value] and not SUPPORT_VISIONS[other.vision]
                                and canSwap(held, slot, other)
                        end)
                        if taken ~= nil then
                            given = slot
                            break
                        end
                    end
                end
                if given == nil then
                    log(string.format("Only %d healing abilities for %s.", healingCount(pool, vision),
                        itemName(vision)))
                    break
                end
                swapAbilities(held, given, taken)
            end
        end
    end

    local changed = 0
    local byVision, visions = {}, {}
    for _, slot in ipairs(slots) do
        if slot.value ~= slot.original then
            changed = changed + 1
            addPatch(plan, slot.family, slot.row, "params", slot.value, "detailData", slot.index, 1)
        end
        if byVision[slot.vision] == nil then
            byVision[slot.vision] = { active = {}, passive = {} }
            table.insert(visions, slot.vision)
        end
        table.insert(byVision[slot.vision][slot.kind], abilityName(slot.value))
    end
    table.sort(visions, function(a, b) return itemName(a) < itemName(b) end)
    for _, vision in ipairs(visions) do
        addSpoiler(plan, "Vision abilities", string.format("%s\n  Abilities: %s\n  Passives: %s", itemName(vision),
            table.concat(byVision[vision].active, ", "), table.concat(byVision[vision].passive, ", ")))
    end
    log(string.format("Vision abilities: %d slots, %d changed, %s, %d clashes left.", #slots, changed,
        settings.mixAbilities and "Physical and Magic mixed" or "Physical and Magic apart", clashes))
end

-- The 26 finishers trade places among the Visions.
local function shuffleFinishers(plan, settings, tables)
    local slots = {}
    for _, name in ipairs(sortedRowNames(tables.visionItems)) do
        local row = tables.visionItems:FindRow(name)
        if row.finishBlowSkill > 0 then
            table.insert(slots, { row = name, vision = row.ID, original = row.finishBlowSkill })
        end
    end
    local values = {}
    for i, slot in ipairs(slots) do
        values[i] = slot.original
    end
    newRng(settings.seed, "finishers").shuffle(values)
    for i, slot in ipairs(slots) do
        if values[i] ~= slot.original then
            addPatch(plan, "visionItems", slot.row, "finishBlowSkill", values[i])
        end
        addSpoiler(plan, "Finishers", string.format("%-18s %s   (was %s)", itemName(slot.vision),
            abilityName(values[i]), abilityName(slot.original)))
    end
    log(string.format("Finishers: %d shuffled.", #slots))
end

local function writeSpoiler(plan, settings)
    local path = MOD_DIR .. "Spoiler_" .. settings.seed:gsub("[^%w_-]", "_") .. ".txt"
    local file = io.open(path, "w")
    if file == nil then
        log("Could not write spoiler log to " .. path)
        return
    end
    local function onOff(value) return value and "on" or "off" end
    file:write("FFR Randomizer " .. MOD_VERSION .. " spoiler log\n")
    file:write(string.format("Seed: %s\nItems: %s   Shops: %s   Visions: %s   Enemies: %s   Bosses: %s   Chest quantity: %s\n",
        settings.seed, onOff(settings.items), onOff(settings.shops), onOff(settings.visions),
        onOff(settings.enemies), onOff(settings.bosses), settings.chestQuantity))
    file:write(string.format("Vision abilities: %s   Mix Physical and Magic: %s   Finishers: %s   Supports stay supports: %s\n",
        onOff(settings.abilities), onOff(settings.mixAbilities), onOff(settings.finishers), onOff(settings.supports)))
    if settings.enemies or settings.bosses then
        file:write("With enemies or bosses on, drops and steals stay with the spot: a unit's list below\n" ..
            "goes to whichever unit fights in its place.\n")
    end
    for _, section in ipairs({ "Visions", "Vision abilities", "Finishers", "Bosses", "Enemies", "Chests",
        "Drops and steals", "Shops" }) do
        local lines = plan.spoiler[section]
        if lines ~= nil then
            file:write("\n== " .. section .. " ==\n")
            file:write(table.concat(lines, "\n"), "\n")
        end
    end
    file:close()
    log("Spoiler log written to " .. path)
end

local function buildPlan(settings)
    local tables = {}
    for key, path in pairs(TABLES) do
        tables[key] = findTable(path)
        local needed = not OPTIONAL_TABLES[key]
            and (settings.enemies or settings.bosses or not ENEMY_TABLES[key])
            and (settings.abilities or not ABILITY_TABLES[key])
        if tables[key] == nil and needed then
            return nil, path
        end
    end
    local catalog, missing = buildCatalog()
    if catalog == nil then
        return nil, missing
    end
    local consumed = buildConsumed(tables.events, tables.questBoard)

    local plan = newPlan()
    shufflePool(plan, settings, tables, catalog, consumed)
    if settings.shops then
        shuffleShops(plan, settings, tables, catalog, consumed)
    end
    if settings.visions then
        shuffleVisions(plan, settings, tables, catalog)
    end
    if settings.enemies then
        shuffleEnemies(plan, settings, tables)
    end
    if settings.bosses then
        shuffleBosses(plan, settings, tables)
    end
    if settings.abilities then
        shuffleVisionAbilities(plan, settings, tables)
    end
    if settings.finishers then
        shuffleFinishers(plan, settings, tables)
    end
    return plan
end

-- Applying ------------------------------------------------------------------
-- Tables can be rebuilt, so the plan is rechecked on every tick. The index of
-- which family tables hold each row is built once and rebuilt if a table dies.

local function buildIndex(plan)
    local wanted = {}
    for _, patch in ipairs(plan.patches) do
        wanted[patch.family] = wanted[patch.family] or {}
        wanted[patch.family][patch.row] = true
    end
    local index = {}
    for family, rows in pairs(wanted) do
        index[family] = {}
        for _, dataTable in ipairs(tableFamily(findTable(TABLES[family]))) do
            for _, name in ipairs(dataTable:GetRowNames()) do
                name = nameText(name)
                if rows[name] then
                    index[family][name] = index[family][name] or {}
                    table.insert(index[family][name], dataTable)
                end
            end
        end
    end
    return index
end

local function applyPatch(row, patch)
    local target, key = row, patch.field
    if patch.array ~= nil then
        target = row[patch.array]
        if key == nil then
            key = patch.index
        else
            target = target[patch.index]
        end
    end
    if patch.sub ~= nil then
        target, key = target[key], patch.sub
    end
    if target[key] == patch.value then
        return false
    end
    target[key] = patch.value
    return true
end

local settings = readSettings()
local plan, index
local waitingFor
local reportedErrors = {}

local function reportOnce(key, message)
    if not reportedErrors[key] then
        reportedErrors[key] = true
        log(message)
    end
end

local function applyPlan()
    local changed, broken = 0, false
    for _, patch in ipairs(plan.patches) do
        for _, dataTable in ipairs(index[patch.family][patch.row] or {}) do
            if not dataTable:IsValid() then
                broken = true
            else
                local row = dataTable:FindRow(patch.row)
                if row ~= nil then
                    local ok, result = pcall(applyPatch, row, patch)
                    if not ok then
                        local where = patch.row .. "." .. tostring(patch.field or patch.array)
                        reportOnce(patch.family .. where, "Failed on " .. where .. ": " .. tostring(result))
                    elseif result then
                        changed = changed + 1
                    end
                end
            end
        end
    end
    if broken then
        index = nil
    end
    return changed
end

local function tick()
    if plan == nil then
        local missing
        plan, missing = buildPlan(settings)
        if plan == nil then
            if waitingFor ~= missing then
                waitingFor = missing
                log("Waiting for " .. tostring(missing))
            end
            return
        end
        log(string.format("Plan ready: %d changes for seed %s.", #plan.patches, settings.seed))
        writeSpoiler(plan, settings)
    end
    if index == nil then
        index = buildIndex(plan)
    end
    local changed = applyPlan()
    if changed > 0 then
        log(string.format("Applied %d field changes.", changed))
    end
end

log("Version " .. MOD_VERSION .. " loaded. Seed " .. settings.seed .. ". Waiting for game tables.")

LoopAsync(CHECK_INTERVAL_MS, function()
    ExecuteInGameThread(function()
        local ok, err = pcall(tick)
        if not ok then
            reportOnce("tick" .. tostring(err), "Error: " .. tostring(err))
        end
    end)
    return false
end)
