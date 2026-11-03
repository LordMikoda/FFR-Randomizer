-- FFR Randomizer for UE4SS.
-- Reads Randomizer.txt, builds a seeded plan from the game's own tables and
-- applies it in memory. Nothing on disk in the game changes, and removing this
-- folder undoes everything.

local MOD_VERSION = "0.1.1"
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

local TABLES = {
    chests = "/Game/Datatable/Map/Gimmick/TreasureBox/CDT_MapGimmickTreasureBox.CDT_MapGimmickTreasureBox",
    units = "/Game/Datatable/Unit/DT_UnitParameter.DT_UnitParameter",
    shops = "/Game/Datatable/Shop/DT_ShopList.DT_ShopList",
    events = "/Game/Datatable/Event/TalkEvent/CDT_TalkEventData_Prod.CDT_TalkEventData_Prod",
    questBoard = "/Game/Datatable/Quest/CDT_QuestBoardData.CDT_QuestBoardData",
}

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

local VISION_COUNT = 26
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
        onOff(settings.shops), onOff(settings.visions), settings.chestQuantity))
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

    local function flag(key)
        local value = (values[key] or "on"):lower()
        return value ~= "off" and value ~= "false" and value ~= "0"
    end

    local settings = {
        seed = values.seed or "",
        items = flag("items"),
        shops = flag("shops"),
        visions = flag("visions"),
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
-- A patch sets one field: row[field], or row[array][index][field].

local function newPlan()
    return { patches = {}, spoiler = {} }
end

local function addPatch(plan, family, row, field, value, array, index)
    table.insert(plan.patches, {
        family = family, row = row, field = field, value = value, array = array, index = index,
    })
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

local function writeSpoiler(plan, settings)
    local path = MOD_DIR .. "Spoiler_" .. settings.seed:gsub("[^%w_-]", "_") .. ".txt"
    local file = io.open(path, "w")
    if file == nil then
        log("Could not write spoiler log to " .. path)
        return
    end
    local function onOff(value) return value and "on" or "off" end
    file:write("FFR Randomizer " .. MOD_VERSION .. " spoiler log\n")
    file:write(string.format("Seed: %s\nItems: %s   Shops: %s   Visions: %s   Chest quantity: %s\n",
        settings.seed, onOff(settings.items), onOff(settings.shops), onOff(settings.visions),
        settings.chestQuantity))
    for _, section in ipairs({ "Visions", "Chests", "Drops and steals", "Shops" }) do
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
        if tables[key] == nil and key ~= "questBoard" then
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
    local target = row
    if patch.array ~= nil then
        target = row[patch.array][patch.index]
    end
    if target[patch.field] == patch.value then
        return false
    end
    target[patch.field] = patch.value
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
                        reportOnce(patch.family .. patch.row .. patch.field,
                            "Failed on " .. patch.row .. "." .. patch.field .. ": " .. tostring(result))
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
