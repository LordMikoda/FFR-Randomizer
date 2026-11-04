-- FFR Randomizer for UE4SS.
-- Reads Randomizer.txt, builds a seeded plan from the game's own tables and
-- applies it in memory. Nothing on disk in the game changes, and removing this
-- folder undoes everything.

local MOD_VERSION = "0.8.0"
local LOG_PREFIX = "[FFRRandomizer] "
local CHECK_INTERVAL_MS = 3000

local function log(message)
    print(LOG_PREFIX .. message .. "\n")
end

local MOD_DIR = debug.getinfo(1, "S").source:match("^@(.*[\\/])Scripts[\\/]main%.lua$")
    or "ue4ss/Mods/FFRRandomizer/"
local SETTINGS_FILE = MOD_DIR .. "Randomizer.txt"
-- What happened at the last game start, for the app to show.
local STATUS_FILE = MOD_DIR .. "Status.txt"

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

local hasMoves, CHARACTER_MOVES = pcall(require, "CharacterMoves")
if not hasMoves or type(CHARACTER_MOVES) ~= "table" then
    CHARACTER_MOVES = nil
end

local hasChaosData, CHAOS_DATA = pcall(require, "ChaosData")
if not hasChaosData or type(CHAOS_DATA) ~= "table" then
    CHAOS_DATA = { elements = {}, statuses = {}, stats = {}, passives = {} }
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
    equipment = "/Game/Datatable/Item/DT_EquipmentItemData.DT_EquipmentItemData",
    playables = "/Game/Datatable/Unit/DT_PlayableUnitParameter.DT_PlayableUnitParameter",
    maps = "/Game/Datatable/Map/CDT_MapData_Prod.CDT_MapData_Prod",
    difficulty = "/Game/Datatable/Difficulty/DT_GameDifficultyParam.DT_GameDifficultyParam",
}
-- Tables only some options need, by the setting that needs them.
local NEEDED_BY = {
    playables = function(settings) return settings.swapGear end,
    maps = function(settings) return settings.encounterRate ~= "normal" end,
    difficulty = function(settings) return settings.expRate ~= 1 or settings.gilRate ~= 1 or settings.mrpRate ~= 1 end,
}
-- Tables the plan can do without, the ones only the enemy and boss shuffles
-- need, and the ones only the Vision ability shuffle needs.
local OPTIONAL_TABLES = { questBoard = true }
local ENEMY_TABLES = { battleGroups = true, unitDesign = true }
local ABILITY_TABLES = { awakening = true, synchro = true, passives = true }

-- Each playable character's level table, keyed "level" .. its suffix, such as
-- levelRain. Only the character shuffles need them.
local CHARACTER_TABLES = {}
for _, character in ipairs(CHARACTER_MOVES and CHARACTER_MOVES.characters or {}) do
    local name = "DT_UnitLevelParameter" .. character.table
    TABLES["level" .. character.table] = "/Game/Datatable/Unit/LevelParameter/Playable/" .. name .. "." .. name
    CHARACTER_TABLES["level" .. character.table] = true
end
-- The stats Random character stats trades between. HP and MP stay put.
local CHARACTER_STATS = { "Attack", "Defence", "Intelligence", "Mind", "Agility" }

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
-- With Enemies at full strength, a swapped-in enemy or boss keeps its own
-- stats and takes only the spot's rewards, so EXP and Gil still fit the area.
local ENEMY_REWARD_FIELDS = {
    "Experience", "Money",
    "DropNormalItem", "DropNormalItemRate", "DropRareItem", "DropRareItemRate",
    "StealNormalItem", "StealRareItem", "StealItemRate",
}
-- Story items in the shuffle adds these kinds, and the items quests take.
local STORY_KINDS = { Valuable = true, Artifact = true }
-- Encounter rate stretches or shrinks the distance walked between battles.
local ENCOUNTER_FIELDS = { "encountMinMovementLength", "encountMaxMovementLength",
    "chocoboEncountMinMovementLength", "chocoboEncountMaxMovementLength" }
local ENCOUNTER_SCALE = { normal = 1, half = 2, double = 0.5 }
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

-- Leah, Ayaka and Y'shtola among the Visions, and Fina among the characters.
-- With supports on, each keeps at least SUPPORT_MIN_HEALING abilities from
-- HEALING_SKILLS.
local SUPPORTS = { [13045] = true, [13051] = true, [13127] = true, [1003] = true }
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
# Characters. mix_character_moves only matters with character_moves or character_finishers on.
character_moves=%s
character_finishers=%s
mix_character_moves=%s
character_stats=%s
# Gear and enemies.
gear_stats=%s
gear_passives=%s
enemy_weaknesses=%s
status_resistances=%s
enemy_passives=%s
# normal, half or double
encounter_rate=%s
# We are Chaos. These can make a run unwinnable.
random_stats=%s
swap_gear_types=%s
full_strength=%s
shops_sell_anything=%s
story_items=%s
# default, 9 or 50. Weapons, armor and accessories always stay at 1.
chest_quantity=%s
# Rewards from every battle: 0.25 to 10 in steps of 0.25. 1 is the game's own.
exp_rate=%s
gil_rate=%s
mrp_rate=%s
]]

local function trim(text)
    return (text:gsub("^%s+", ""):gsub("%s+$", ""))
end

-- A reward rate as the settings file writes it: 1, 2.5, 0.25.
local function rateText(rate)
    return string.format("%g", rate)
end

-- 0.25 to 10 in steps of 0.25; anything else is pulled to the nearest step.
local function readRate(text, key)
    local value = tonumber(text or "1")
    if value == nil then
        log("Unknown " .. key .. " '" .. tostring(text) .. "', using 1.")
        return 1
    end
    value = math.floor(value * 4 + 0.5) / 4
    return math.max(0.25, math.min(10, value))
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
        onOff(settings.supports), onOff(settings.characterMoves), onOff(settings.characterFinishers),
        onOff(settings.mixCharacters), onOff(settings.characterStats),
        onOff(settings.gearStats), onOff(settings.gearPassives), onOff(settings.weaknesses),
        onOff(settings.statusResists), onOff(settings.enemyPassives), settings.encounterRate,
        onOff(settings.randomStats), onOff(settings.swapGear), onOff(settings.fullStrength),
        onOff(settings.shopsAnything), onOff(settings.storyItems), settings.chestQuantity,
        rateText(settings.expRate), rateText(settings.gilRate), rateText(settings.mrpRate)))
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
        characterMoves = flag("character_moves", "off"),
        characterFinishers = flag("character_finishers", "off"),
        mixCharacters = flag("mix_character_moves", "off"),
        characterStats = flag("character_stats", "off"),
        gearStats = flag("gear_stats", "off"),
        gearPassives = flag("gear_passives", "off"),
        weaknesses = flag("enemy_weaknesses", "off"),
        statusResists = flag("status_resistances", "off"),
        enemyPassives = flag("enemy_passives", "off"),
        encounterRate = (values.encounter_rate or "normal"):lower(),
        randomStats = flag("random_stats", "off"),
        swapGear = flag("swap_gear_types", "off"),
        fullStrength = flag("full_strength", "off"),
        shopsAnything = flag("shops_sell_anything", "off"),
        storyItems = flag("story_items", "off"),
        chestQuantity = (values.chest_quantity or "default"):lower(),
        expRate = readRate(values.exp_rate, "exp_rate"),
        gilRate = readRate(values.gil_rate, "gil_rate"),
        mrpRate = readRate(values.mrp_rate, "mrp_rate"),
    }
    if CHEST_CAPS[settings.chestQuantity] == nil then
        log("Unknown chest_quantity '" .. settings.chestQuantity .. "', using default.")
        settings.chestQuantity = "default"
    end
    if ENCOUNTER_SCALE[settings.encounterRate] == nil then
        log("Unknown encounter_rate '" .. settings.encounterRate .. "', using normal.")
        settings.encounterRate = "normal"
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

-- Settings code ---------------------------------------------------------------
-- The seed and every option in one line, such as FFR2-0631G07ZQZ-49353958654:
-- FFR and the format number, the options as bits in 10 Crockford base32
-- characters, then the seed as typed. Must stay in step with SettingsCode in
-- the app. Bits 0 to 22 are the switches in CODE_TOGGLES' order, 23 and 24 the
-- encounter rate, 25 and 26 the chest quantity, 27 and 28 kept for trapped
-- chests (always 0; the game's chest traps couldn't start a fight), then
-- six bits each for the EXP, Gil and MRP rates as quarter steps above 0.25
-- (1x is 3). Bits 47 to 49 are free. FFR1 (0.7.0) had 7 characters and no
-- traps or rates.

local CODE_TOGGLES = {
    "items", "shops", "visions", "enemies", "bosses",
    "abilities", "mixAbilities", "finishers", "supports",
    "characterMoves", "characterFinishers", "mixCharacters", "characterStats",
    "gearStats", "gearPassives", "weaknesses", "statusResists", "enemyPassives",
    "randomStats", "swapGear", "fullStrength", "shopsAnything", "storyItems",
}
local CODE_ENCOUNTERS = { normal = 0, half = 1, double = 2 }
local CODE_QUANTITIES = { default = 0, ["9"] = 1, ["50"] = 2 }
local CODE_DIGITS = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"

local function settingsCode(settings)
    local bits = 0
    for i, key in ipairs(CODE_TOGGLES) do
        if settings[key] then
            bits = bits | (1 << (i - 1))
        end
    end
    bits = bits | (CODE_ENCOUNTERS[settings.encounterRate] << 23) | (CODE_QUANTITIES[settings.chestQuantity] << 25)
    for i, rate in ipairs({ settings.expRate, settings.gilRate, settings.mrpRate }) do
        bits = bits | (math.floor(rate * 4 + 0.5) - 1) << (29 + 6 * (i - 1))
    end
    local chars = {}
    for i = 9, 0, -1 do
        local digit = (bits >> (5 * i)) & 31
        table.insert(chars, CODE_DIGITS:sub(digit + 1, digit + 1))
    end
    return "FFR2-" .. table.concat(chars) .. "-" .. settings.seed
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

-- TMap fields (gear stats, resistances). UE4SS hands keys and values to
-- ForEach wrapped, with :get() giving the plain value; Find may too.
local function plainValue(value)
    if type(value) == "userdata" or type(value) == "table" then
        local ok, plain = pcall(function() return value:get() end)
        if ok and plain ~= nil then
            return plain
        end
    end
    return value
end

-- A map's entries sorted by key, so a shuffle never depends on map order.
local function mapEntries(map)
    local entries = {}
    if map == nil then
        return entries
    end
    map:ForEach(function(key, value)
        table.insert(entries, { key = plainValue(key), value = plainValue(value) })
    end)
    table.sort(entries, function(a, b) return a.key < b.key end)
    return entries
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

-- A unit in a spoiler line. The line holds the row key between \1 marks, so
-- the seed check never depends on the game's language; writeSpoiler swaps in
-- the unit's name, read from the row's Name text while the game runs, or
-- keeps the row key when there is none.
local unitTable
local unitLabels = {}
local unitNamesRead, unitNamesMissing = 0, 0

local function unitLabel(units, rowName, id)
    unitTable = units
    return string.format("\1%s\1 (unit %d)", rowName, id)
end

local function unitDisplayName(rowName)
    local label = unitLabels[rowName]
    if label == nil then
        local text
        local ok = pcall(function()
            local name = unitTable:FindRow(rowName).Name
            if name ~= nil then
                text = type(name) == "string" and name or name:ToString()
            end
        end)
        if ok and type(text) == "string" and text:match("%S") then
            unitNamesRead = unitNamesRead + 1
            label = trim(text)
        else
            unitNamesMissing = unitNamesMissing + 1
            label = rowName
        end
        unitLabels[rowName] = label
    end
    return label
end

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

-- Sets one existing key of the TMap row[field].
local function addMapPatch(plan, family, row, field, mapKey, value)
    local key = table.concat({ family, row, field, "map", tostring(mapKey) }, "|")
    local existing = plan.patchIndex[key]
    if existing ~= nil then
        existing.value = value
        return
    end
    local patch = { family = family, row = row, field = field, value = value, mapKey = mapKey }
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
    -- Story items in the shuffle lets key items, artifacts and quest items move too.
    local function canMove(id)
        local info = catalog[id]
        if info == nil then
            return false
        end
        if settings.storyItems then
            return SHUFFLE_KINDS[info.kind] or STORY_KINDS[info.kind]
        end
        return SHUFFLE_KINDS[info.kind] and not consumed[id]
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
            addSpoiler(plan, "Drops and steals", string.format("%s, %s: %s   (was %s)",
                unitLabel(tables.units, slot.row, slot.unitId), slot.field, itemName(item), itemName(slot.original)))
        end
    end
    log(string.format("Item pool: %d slots, %d moved, chest quantity %s.", #slots, moved,
        settings.chestQuantity))
end

local function itemCategory(info)
    if info.kind == "Equipment" then
        return EQUIPMENT_TYPES[info.equipmentType] or ("Equipment " .. tostring(info.equipmentType))
    end
    return info.kind
end

-- Each slot keeps its category, unlock condition, order limit and price ratio.
-- Slots selling an item that can't be sold, or that a quest takes, stay as they are.
-- Shops sell anything drops the categories and the sale and quest checks:
-- any consumable, gear or seed with a price can turn up in any slot.
local function shuffleShops(plan, settings, tables, catalog, consumed)
    local function canSell(id)
        local info = catalog[id]
        if info == nil or not SHUFFLE_KINDS[info.kind] or info.price <= 0 then
            return false
        end
        return settings.shopsAnything or (info.sellable and not consumed[id])
    end
    local function shopCategory(info)
        if settings.shopsAnything then
            return "Any"
        end
        return itemCategory(info)
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

-- The swapped-in unit's row takes the replaced unit's spot fields: stats and
-- rewards, or only rewards with Enemies at full strength. Drops and steals
-- come from the item shuffle when it moved them.
local function moveSpot(plan, settings, tables, rowOf, id, new)
    local source = tables.units:FindRow(rowOf[id])
    local items = plan.unitItems[rowOf[id]] or {}
    for _, field in ipairs(settings.fullStrength and ENEMY_REWARD_FIELDS or ENEMY_SPOT_FIELDS) do
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
            moveSpot(plan, settings, tables, rowOf, id, new)
        end
        addSpoiler(plan, "Enemies", string.format("%s fights in place of %s, size %s",
            unitLabel(tables.units, rowOf[new], new), unitLabel(tables.units, rowOf[id], id),
            UNIT_SIZES[sizeOf[id]] or "Unknown"))
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
            moveSpot(plan, settings, tables, rowOf, id, new)
        end
        addSpoiler(plan, "Bosses", string.format("%s fights in place of %s, size %s to %s",
            unitLabel(tables.units, rowOf[new], new), unitLabel(tables.units, rowOf[id], id),
            UNIT_SIZES[sizeOf[new]] or "Unknown", UNIT_SIZES[sizeOf[id]] or "Unknown"))
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
                table.insert(found, { family = family, row = name, index = i, owner = row.ID,
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

-- held[owner][key] counts what each owner (a Vision, or a character's unit)
-- holds, so a swap can be checked for clashes.
local function holdAbility(held, slot, change)
    local key = abilityKey(slot, slot.value)
    held[slot.owner][key] = (held[slot.owner][key] or 0) + change
end

-- Whether swapping a's and b's abilities leaves neither owner with a clash.
local function canSwap(held, a, b)
    if a.owner == b.owner then
        return false
    end
    local keyA, keyB = abilityKey(a, a.value), abilityKey(b, b.value)
    return keyA ~= keyB and (held[a.owner][keyB] or 0) == 0 and (held[b.owner][keyA] or 0) == 0
end

local function swapAbilities(held, a, b)
    holdAbility(held, a, -1)
    holdAbility(held, b, -1)
    a.value, b.value = b.value, a.value
    holdAbility(held, a, 1)
    holdAbility(held, b, 1)
end

-- The first slot in the pool, from a random start, that passes the test.
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

local function healingCount(pool, owner)
    local count = 0
    for _, slot in ipairs(pool) do
        if slot.owner == owner and HEALING_SKILLS[slot.value] then
            count = count + 1
        end
    end
    return count
end

-- Shuffles each slot's ability within its pool, groupOf(slot) naming the pool,
-- so every owner keeps its number of slots of each kind and the level they
-- come at. A repair pass then clears any owner holding one ability twice, or
-- two passives from one stat family; the same ability on two owners is fine,
-- as in the base game. With supports on, each support owner takes healing
-- from non-supports in its "active" or "move" pool, giving back one of its
-- other abilities. Returns the clashes the repair couldn't clear.
local function shuffleOwnedSlots(slots, groupOf, rng, supports, nameOf)
    local pools, poolNames, held = {}, {}, {}
    for _, slot in ipairs(slots) do
        local name = groupOf(slot) .. " " .. slot.kind
        slot.pool = name
        if pools[name] == nil then
            pools[name] = {}
            table.insert(poolNames, name)
        end
        table.insert(pools[name], slot)
        held[slot.owner] = held[slot.owner] or {}
    end
    table.sort(poolNames)

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
            while held[slot.owner][abilityKey(slot, slot.value)] > 1 do
                local other = findSwap(pool, rng, function(other) return canSwap(held, slot, other) end)
                if other == nil then
                    clashes = clashes + 1
                    break
                end
                swapAbilities(held, slot, other)
            end
        end
    end

    if supports then
        local owners, poolOf = {}, {}
        for _, slot in ipairs(slots) do
            if SUPPORTS[slot.owner] and (slot.kind == "active" or slot.kind == "move") and poolOf[slot.owner] == nil then
                poolOf[slot.owner] = pools[slot.pool]
                table.insert(owners, slot.owner)
            end
        end
        table.sort(owners)
        for _, owner in ipairs(owners) do
            local pool = poolOf[owner]
            while healingCount(pool, owner) < SUPPORT_MIN_HEALING do
                local given, taken
                for _, slot in ipairs(pool) do
                    if slot.owner == owner and not HEALING_SKILLS[slot.value] then
                        taken = findSwap(pool, rng, function(other)
                            return HEALING_SKILLS[other.value] and not SUPPORTS[other.owner]
                                and canSwap(held, slot, other)
                        end)
                        if taken ~= nil then
                            given = slot
                            break
                        end
                    end
                end
                if given == nil then
                    log(string.format("Only %d healing abilities for %s.", healingCount(pool, owner), nameOf(owner)))
                    break
                end
                swapAbilities(held, given, taken)
            end
        end
    end
    return clashes
end

-- Abilities and passives swap among the Visions, Physical and Magic apart
-- unless mixing is on.
local function shuffleVisionAbilities(plan, settings, tables)
    if VISION_ABILITIES == nil then
        log("VisionAbilities.lua is missing. Vision abilities not shuffled.")
        return
    end
    local slots = readAbilitySlots(tables)
    local function groupOf(slot)
        return settings.mixAbilities and "All" or (VISION_ABILITIES.physical[slot.owner] and "Physical" or "Magic")
    end
    local clashes = shuffleOwnedSlots(slots, groupOf, newRng(settings.seed, "abilities"), settings.supports, itemName)

    local changed = 0
    local byVision, visions = {}, {}
    for _, slot in ipairs(slots) do
        if slot.value ~= slot.original then
            changed = changed + 1
            addPatch(plan, slot.family, slot.row, "params", slot.value, "detailData", slot.index, 1)
        end
        if byVision[slot.owner] == nil then
            byVision[slot.owner] = { active = {}, passive = {} }
            table.insert(visions, slot.owner)
        end
        table.insert(byVision[slot.owner][slot.kind], abilityName(slot.value))
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

-- Characters ----------------------------------------------------------------

local function characterName(unit)
    for _, character in ipairs(CHARACTER_MOVES.characters) do
        if character.unit == unit then
            return character.name
        end
    end
    return "Unit " .. tostring(unit)
end

local function moveName(id)
    return CHARACTER_MOVES.names[id] or abilityName(id)
end

-- Level-up moves and finishers swap between characters' level rows, so each
-- character keeps how many it learns and at which levels, including the ones
-- before a late join. Moves and finishers each shuffle among characters of the
-- same attack type unless mixing is on. Skills in CHARACTER_MOVES.fixed (Lid's
-- Mechabo and Darkfina's Witching Hour pair) stay put. With supports on, Fina
-- keeps her healing like the support Visions.
local function shuffleCharacters(plan, settings, tables)
    if CHARACTER_MOVES == nil then
        log("CharacterMoves.lua is missing. Characters not shuffled.")
        return
    end
    local slots, physical, missing = {}, {}, {}
    for _, character in ipairs(CHARACTER_MOVES.characters) do
        physical[character.unit] = character.physical
        local family = "level" .. character.table
        local dataTable = tables[family]
        if dataTable == nil then
            table.insert(missing, character.name)
        else
            for _, name in ipairs(sortedRowNames(dataTable)) do
                local row = dataTable:FindRow(name)
                for i, id in ipairs(arrayItems(row.AddSkills)) do
                    if id > 0 and not CHARACTER_MOVES.fixed[id] then
                        local kind = CHARACTER_MOVES.finishers[id] and "finisher" or "move"
                        if (kind == "move" and settings.characterMoves) or (kind == "finisher" and settings.characterFinishers) then
                            table.insert(slots, { family = family, row = name, index = i, owner = character.unit,
                                level = row.Level, kind = kind, original = id, value = id })
                        end
                    end
                end
            end
        end
    end
    if #missing > 0 then
        log("Level tables not found for " .. table.concat(missing, ", ") .. ". Their moves stay put.")
    end
    local function groupOf(slot)
        return settings.mixCharacters and "All" or (physical[slot.owner] and "Physical" or "Magic")
    end
    local clashes = shuffleOwnedSlots(slots, groupOf, newRng(settings.seed, "characters"),
        settings.supports and settings.characterMoves, characterName)

    local skillRow, ownerOf = {}, {}
    if settings.characterFinishers then
        for _, name in ipairs(sortedRowNames(tables.skills)) do
            local id = tables.skills:FindRow(name).ID
            skillRow[id] = skillRow[id] or name
        end
    end
    for _, character in ipairs(CHARACTER_MOVES.characters) do
        ownerOf[character.unit] = character.owner
    end

    local changed = 0
    local learned = {}
    for _, slot in ipairs(slots) do
        if slot.value ~= slot.original then
            changed = changed + 1
            addPatch(plan, slot.family, slot.row, nil, slot.value, "AddSkills", slot.index)
        end
        -- The Limit Burst menu lists the finishers whose hasUnit is the
        -- character, learned or not, so a moved finisher takes its new owner's.
        if slot.kind == "finisher" and slot.value ~= slot.original and skillRow[slot.value] ~= nil then
            addPatch(plan, "skills", skillRow[slot.value], "hasUnit", ownerOf[slot.owner])
        end
        learned[slot.owner] = learned[slot.owner] or {}
        table.insert(learned[slot.owner], slot)
    end
    for _, character in ipairs(CHARACTER_MOVES.characters) do
        local list = learned[character.unit]
        if list ~= nil then
            table.sort(list, function(a, b)
                if a.level ~= b.level then return a.level < b.level end
                return a.index < b.index
            end)
            local parts = {}
            for _, slot in ipairs(list) do
                table.insert(parts, string.format("L%d %s%s", slot.level, moveName(slot.value),
                    slot.kind == "finisher" and " (finisher)" or ""))
            end
            addSpoiler(plan, "Characters", character.name .. "\n  " .. table.concat(parts, ", "))
        end
    end
    log(string.format("Characters: %d move and finisher slots, %d changed, %s, %d clashes left.", #slots, changed,
        settings.mixCharacters and "Physical and Magic mixed" or "Physical and Magic apart", clashes))
end

-- Each character's Attack, Defence, Intelligence, Mind and Agility trade
-- places, base stats and level-up gains alike, so the total stays the same
-- and HP and MP are untouched. Rain might grow Intelligence on his Attack
-- curve, for example. With Fully random stats, every stat curve, HP and MP
-- included, comes from any character's matching kind of curve, with no budget.
local function shuffleCharacterStats(plan, settings, tables)
    if CHARACTER_MOVES == nil then
        log("CharacterMoves.lua is missing. Character stats not shuffled.")
        return
    end
    local unitRow = {}
    for _, name in ipairs(sortedRowNames(tables.units)) do
        local row = tables.units:FindRow(name)
        unitRow[row.ID] = unitRow[row.ID] or name
    end
    local characters = {}
    for _, character in ipairs(CHARACTER_MOVES.characters) do
        if tables["level" .. character.table] ~= nil then
            table.insert(characters, character)
        end
    end
    -- Each target stat's source: a character and one of that character's stats.
    local rng = newRng(settings.seed, settings.randomStats and "random stats" or "stats")
    local function sourcesFor(character)
        local sources = {}
        if settings.randomStats then
            for _, stat in ipairs(CHARACTER_STATS) do
                local from = characters[rng.int(#characters)]
                sources[stat] = { character = from, stat = CHARACTER_STATS[rng.int(#CHARACTER_STATS)] }
            end
            for _, stat in ipairs({ "MaxHitPoint", "MaxMagicPoint" }) do
                sources[stat] = { character = characters[rng.int(#characters)], stat = stat }
            end
        else
            local from = { table.unpack(CHARACTER_STATS) }
            rng.shuffle(from)
            for i, stat in ipairs(CHARACTER_STATS) do
                sources[stat] = { character = character, stat = from[i] }
            end
        end
        return sources
    end
    local targets = settings.randomStats
        and { "Attack", "Defence", "Intelligence", "Mind", "Agility", "MaxHitPoint", "MaxMagicPoint" }
        or CHARACTER_STATS

    for _, character in ipairs(characters) do
        local sources = sourcesFor(character)
        local family = "level" .. character.table
        for _, name in ipairs(sortedRowNames(tables[family])) do
            for _, stat in ipairs(targets) do
                local source = sources[stat]
                local from = tables["level" .. source.character.table]:FindRow(name)
                if from ~= nil then
                    addPatch(plan, family, name, "Add" .. stat, from["Add" .. source.stat])
                end
            end
        end
        local base = unitRow[character.unit]
        if base ~= nil then
            for _, stat in ipairs(targets) do
                local source = sources[stat]
                local fromRow = unitRow[source.character.unit]
                if fromRow ~= nil then
                    addPatch(plan, "units", base, stat, tables.units:FindRow(fromRow)[source.stat])
                end
            end
        end
        local parts = {}
        for _, stat in ipairs(targets) do
            local source = sources[stat]
            if source.character ~= character or source.stat ~= stat then
                local owner = source.character == character and "" or (source.character.name .. "'s ")
                table.insert(parts, stat .. " grows like " .. owner .. source.stat)
            end
        end
        addSpoiler(plan, "Character stats", string.format("%-15s %s", character.name,
            #parts > 0 and table.concat(parts, ", ") or "unchanged"))
    end
    log(string.format("Character stats: %d characters %s.", #characters,
        settings.randomStats and "fully randomized" or "reshuffled"))
end

-- Gear and enemies --------------------------------------------------------

local GEAR_TYPE_NAMES = { [1] = "Great Sword", [2] = "Katana", [3] = "Bow", [4] = "Hammer", [5] = "Grimoire",
    [6] = "Gun", [7] = "Rod", [8] = "Whip", [9] = "Heavy Armor", [10] = "Light Armor", [11] = "Robe" }

local function passiveName(id)
    return CHAOS_DATA.passives[id] or abilityName(id)
end

-- Gear of one kind (Great Swords, Robes and so on) with the same stat keys
-- trades whole stat sets, so a Bronze Sword might hit like a Crimson Saber.
-- Prices stay, so a cheap shop sword can be strong.
local function shuffleGearStats(plan, settings, tables)
    local groups, groupNames = {}, {}
    local ok, err = pcall(function()
        for _, name in ipairs(sortedRowNames(tables.equipment)) do
            local row = tables.equipment:FindRow(name)
            local entries = mapEntries(row.params)
            if #entries > 0 then
                local keys = {}
                for _, entry in ipairs(entries) do
                    table.insert(keys, tostring(entry.key))
                end
                local group = tostring(row.equipmentDetailType) .. ":" .. table.concat(keys, ",")
                if groups[group] == nil then
                    groups[group] = {}
                    table.insert(groupNames, group)
                end
                table.insert(groups[group], { row = name, id = row.ID, entries = entries })
            end
        end
    end)
    if not ok then
        log("Gear stats can't be read (" .. tostring(err) .. "). Gear stats not shuffled.")
        return
    end
    table.sort(groupNames)
    local rng = newRng(settings.seed, "gear stats")
    local total, changed = 0, 0
    for _, group in ipairs(groupNames) do
        local items = groups[group]
        local order = {}
        for i = 1, #items do
            order[i] = i
        end
        rng.shuffle(order)
        for i, item in ipairs(items) do
            total = total + 1
            local source = items[order[i]]
            if source ~= item then
                changed = changed + 1
                local stats = {}
                for j, entry in ipairs(item.entries) do
                    addMapPatch(plan, "equipment", item.row, "params", entry.key, source.entries[j].value)
                    table.insert(stats, string.format("%s %s", CHAOS_DATA.stats[entry.key] or ("Stat " .. tostring(entry.key)),
                        tostring(source.entries[j].value)))
                end
                addSpoiler(plan, "Gear stats", string.format("%s: %s (stats of %s)", itemName(item.id),
                    table.concat(stats, ", "), itemName(source.id)))
            end
        end
    end
    log(string.format("Gear stats: %d pieces of gear, %d changed.", total, changed))
end

-- Passives on gear swap among gear of the same slot (weapons, armor,
-- accessories), each piece keeping its number of passives, never one twice.
local function shuffleGearPassives(plan, settings, tables)
    local slots = {}
    for _, name in ipairs(sortedRowNames(tables.equipment)) do
        local row = tables.equipment:FindRow(name)
        for i, id in ipairs(arrayItems(row.passiveList)) do
            if id > 0 then
                table.insert(slots, { row = name, index = i, owner = row.ID, kind = "gear",
                    slotType = row.EquipmentType, original = id, value = id })
            end
        end
    end
    local clashes = shuffleOwnedSlots(slots, function(slot) return "type " .. tostring(slot.slotType) end,
        newRng(settings.seed, "gear passives"), false, itemName)
    local changed, byItem, order = 0, {}, {}
    for _, slot in ipairs(slots) do
        if slot.value ~= slot.original then
            changed = changed + 1
            addPatch(plan, "equipment", slot.row, nil, slot.value, "passiveList", slot.index)
        end
        if byItem[slot.owner] == nil then
            byItem[slot.owner] = { now = {}, was = {}, moved = false }
            table.insert(order, slot.owner)
        end
        local item = byItem[slot.owner]
        table.insert(item.now, passiveName(slot.value))
        table.insert(item.was, passiveName(slot.original))
        item.moved = item.moved or slot.value ~= slot.original
    end
    for _, id in ipairs(order) do
        local item = byItem[id]
        if item.moved then
            addSpoiler(plan, "Gear passives", string.format("%s: %s   (was %s)", itemName(id),
                table.concat(item.now, ", "), table.concat(item.was, ", ")))
        end
    end
    log(string.format("Gear passives: %d slots, %d changed, %d clashes left.", #slots, changed, clashes))
end

-- Within each unit, a resistance map's values trade places between its keys,
-- so a Fire-weak enemy might be weak to Ice instead, with the same overall
-- profile. Only units in the given categories change.
local function shuffleResistances(plan, settings, tables, field, categories, stream, section, names)
    local rng = newRng(settings.seed, stream)
    local units, changed = 0, 0
    local ok, err = pcall(function()
        for _, name in ipairs(sortedRowNames(tables.units)) do
            local row = tables.units:FindRow(name)
            if categories[row.Category] then
                local entries = mapEntries(row[field])
                if #entries >= 2 then
                    units = units + 1
                    local values = {}
                    for i, entry in ipairs(entries) do
                        values[i] = entry.value
                    end
                    rng.shuffle(values)
                    local parts, moved = {}, false
                    for i, entry in ipairs(entries) do
                        if values[i] ~= entry.value then
                            moved = true
                            addMapPatch(plan, "units", name, field, entry.key, values[i])
                        end
                        table.insert(parts, string.format("%s %s", names[entry.key] or tostring(entry.key), tostring(values[i])))
                    end
                    if moved then
                        changed = changed + 1
                        addSpoiler(plan, section, string.format("%s: %s", unitLabel(tables.units, name, row.ID),
                            table.concat(parts, ", ")))
                    end
                end
            end
        end
    end)
    if not ok then
        log(section .. " can't be read (" .. tostring(err) .. "). Not shuffled.")
        return
    end
    log(string.format("%s: %d units with two or more entries, %d changed.", section, units, changed))
end

-- Enemy passives swap among the regular enemies, each keeping its number of
-- passives. Bosses keep theirs, since boss passives often run the fight.
local function shuffleEnemyPassives(plan, settings, tables)
    local slots = {}
    for _, name in ipairs(sortedRowNames(tables.units)) do
        local row = tables.units:FindRow(name)
        if row.Category == ENEMY_CATEGORY then
            for i, id in ipairs(arrayItems(row.passiveSkillList)) do
                if id > 0 then
                    table.insert(slots, { row = name, index = i, owner = name, unit = row.ID, kind = "enemy",
                        original = id, value = id })
                end
            end
        end
    end
    local clashes = shuffleOwnedSlots(slots, function() return "enemies" end, newRng(settings.seed, "enemy passives"),
        false, function(owner) return owner end)
    local changed, byUnit, order = 0, {}, {}
    for _, slot in ipairs(slots) do
        if slot.value ~= slot.original then
            changed = changed + 1
            addPatch(plan, "units", slot.row, nil, slot.value, "passiveSkillList", slot.index)
        end
        if byUnit[slot.owner] == nil then
            byUnit[slot.owner] = { unit = slot.unit, now = {}, moved = false }
            table.insert(order, slot.owner)
        end
        table.insert(byUnit[slot.owner].now, passiveName(slot.value))
        byUnit[slot.owner].moved = byUnit[slot.owner].moved or slot.value ~= slot.original
    end
    for _, owner in ipairs(order) do
        local unit = byUnit[owner]
        if unit.moved then
            addSpoiler(plan, "Enemy passives", string.format("%s: %s", unitLabel(tables.units, owner, unit.unit),
                table.concat(unit.now, ", ")))
        end
    end
    log(string.format("Enemy passives: %d slots, %d changed, %d clashes left.", #slots, changed, clashes))
end

-- Characters trade weapon types and armor sets, so Rain might fight with a
-- bow. Starting gear the new types can't use may come off.
local function swapGearTypes(plan, settings, tables)
    local rows = {}
    for _, name in ipairs(sortedRowNames(tables.playables)) do
        local row = tables.playables:FindRow(name)
        table.insert(rows, { name = name, id = row.ID, weapon = row.equipableWeaponType,
            armor = arrayItems(row.equipableArmorTypeList) })
    end
    local order = {}
    for i = 1, #rows do
        order[i] = i
    end
    newRng(settings.seed, "gear types").shuffle(order)
    local function nameOf(id)
        return CHARACTER_MOVES and characterName(id) or ("Unit " .. tostring(id))
    end
    for i, row in ipairs(rows) do
        local source = rows[order[i]]
        addPatch(plan, "playables", row.name, "equipableWeaponType", source.weapon)
        local armor = {}
        for j = 1, #row.armor do
            local value = source.armor[j] or row.armor[j]
            addPatch(plan, "playables", row.name, nil, value, "equipableArmorTypeList", j)
            table.insert(armor, GEAR_TYPE_NAMES[value] or tostring(value))
        end
        addSpoiler(plan, "Gear types", string.format("%-15s %s, %s   (%s's)", nameOf(row.id),
            GEAR_TYPE_NAMES[source.weapon] or tostring(source.weapon), table.concat(armor, " and "), nameOf(source.id)))
    end
    log(string.format("Gear types: %d characters swapped.", #rows))
end

-- Half the rate means walking twice as far between random battles; double
-- means half as far.
local function scaleEncounters(plan, settings, tables)
    local scale = ENCOUNTER_SCALE[settings.encounterRate]
    local maps = 0
    for _, name in ipairs(sortedRowNames(tables.maps)) do
        local row = tables.maps:FindRow(name)
        local touched = false
        for _, field in ipairs(ENCOUNTER_FIELDS) do
            local value = row[field]
            if type(value) == "number" and value > 0 then
                addPatch(plan, "maps", name, field, math.max(1, math.floor(value * scale + 0.5)))
                touched = true
            end
        end
        if touched then
            maps = maps + 1
        end
    end
    addSpoiler(plan, "Encounter rate", string.format("%s: %d maps changed", settings.encounterRate, maps))
    log(string.format("Encounter rate: %s, %d maps changed.", settings.encounterRate, maps))
end

-- Scales EXP, Gil and MRP from every battle on every difficulty, so Expert's
-- own 1.5x MRP stacks with the slider.
local REWARD_FIELDS = { { "ExpRate", "expRate" }, { "MoneyRate", "gilRate" }, { "MRPointRate", "mrpRate" } }

local function scaleRewards(plan, settings, tables)
    local rows = 0
    for _, name in ipairs(sortedRowNames(tables.difficulty)) do
        local row = tables.difficulty:FindRow(name)
        for _, pair in ipairs(REWARD_FIELDS) do
            local value = row[pair[1]]
            if type(value) == "number" then
                addPatch(plan, "difficulty", name, pair[1], value * settings[pair[2]])
            end
        end
        rows = rows + 1
    end
    addSpoiler(plan, "Rewards", string.format("EXP x%s, Gil x%s, MRP x%s on %d difficulties", rateText(settings.expRate),
        rateText(settings.gilRate), rateText(settings.mrpRate), rows))
    log(string.format("Rewards: EXP x%s, Gil x%s, MRP x%s on %d difficulties.", rateText(settings.expRate),
        rateText(settings.gilRate), rateText(settings.mrpRate), rows))
end

local SPOILER_SECTIONS = { "Visions", "Vision abilities", "Finishers", "Characters", "Character stats",
    "Gear types", "Gear stats", "Gear passives", "Bosses", "Enemies", "Enemy weaknesses", "Status resistances",
    "Enemy passives", "Encounter rate", "Rewards", "Chests", "Drops and steals", "Shops" }

-- Four words hashed from the settings code and the spoiler's sections (with
-- unit row keys, not names, so the game's language doesn't change it): two
-- players with the same seed check have the same shuffle. A mod version or
-- game patch that moves anything gives a different one.
local CHECK_WORDS = {
    "Potion", "Hi-Potion", "X-Potion", "Ether", "Elixir", "Megalixir", "Phoenix Down", "Remedy",
    "Antidote", "Eye Drops", "Echo Herb", "Gold Needle", "Tent", "Cottage", "Ribbon", "Excalibur",
    "Masamune", "Mythril", "Adamantite", "Chocobo", "Moogle", "Cactuar", "Tonberry", "Bomb",
    "Behemoth", "Malboro", "Crystal", "Airship", "Gil", "Summon", "Limit", "Esper",
}

local function seedCheck(plan, settings)
    local parts = { settingsCode(settings) }
    for _, section in ipairs(SPOILER_SECTIONS) do
        if plan.spoiler[section] ~= nil then
            table.insert(parts, section)
            table.insert(parts, table.concat(plan.spoiler[section], "\n"))
        end
    end
    local hash = hashText(table.concat(parts, "\n"))
    local words = {}
    for i = 0, 3 do
        table.insert(words, CHECK_WORDS[((hash >> (5 * i)) & 31) + 1])
    end
    return table.concat(words, " / ")
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
    file:write(string.format("Seed: %s\nSettings code: %s\nSeed check: %s\nItems: %s   Shops: %s   Visions: %s   Enemies: %s   Bosses: %s   Chest quantity: %s\n",
        settings.seed, settingsCode(settings), plan.check, onOff(settings.items), onOff(settings.shops),
        onOff(settings.visions),
        onOff(settings.enemies), onOff(settings.bosses), settings.chestQuantity))
    file:write(string.format("Vision abilities: %s   Mix Physical and Magic: %s   Finishers: %s   Supports stay supports: %s\n",
        onOff(settings.abilities), onOff(settings.mixAbilities), onOff(settings.finishers), onOff(settings.supports)))
    file:write(string.format("Character moves: %s   Character finishers: %s   Mix character moves: %s   Character stats: %s\n",
        onOff(settings.characterMoves), onOff(settings.characterFinishers), onOff(settings.mixCharacters),
        onOff(settings.characterStats)))
    file:write(string.format("Gear stats: %s   Gear passives: %s   Enemy weaknesses: %s   Status resistances: %s   Enemy passives: %s   Encounter rate: %s\n",
        onOff(settings.gearStats), onOff(settings.gearPassives), onOff(settings.weaknesses),
        onOff(settings.statusResists), onOff(settings.enemyPassives), settings.encounterRate))
    file:write(string.format("We are Chaos: Fully random stats: %s   Swap gear types: %s   Enemies at full strength: %s   Shops sell anything: %s   Story items: %s\n",
        onOff(settings.randomStats), onOff(settings.swapGear), onOff(settings.fullStrength),
        onOff(settings.shopsAnything), onOff(settings.storyItems)))
    file:write(string.format("Rewards: EXP x%s   Gil x%s   MRP x%s\n", rateText(settings.expRate),
        rateText(settings.gilRate), rateText(settings.mrpRate)))
    if settings.enemies or settings.bosses then
        file:write("With enemies or bosses on, drops and steals stay with the spot: a unit's list below\n" ..
            "goes to whichever unit fights in its place.\n")
    end
    for _, section in ipairs(SPOILER_SECTIONS) do
        local lines = plan.spoiler[section]
        if lines ~= nil then
            file:write("\n== " .. section .. " ==\n")
            file:write((table.concat(lines, "\n"):gsub("\1([^\1]*)\1", unitDisplayName)), "\n")
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
            and (settings.characterMoves or settings.characterFinishers or settings.characterStats
                or settings.randomStats or not CHARACTER_TABLES[key])
            and (NEEDED_BY[key] == nil or NEEDED_BY[key](settings))
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
    if settings.characterMoves or settings.characterFinishers then
        shuffleCharacters(plan, settings, tables)
    end
    if settings.characterStats or settings.randomStats then
        shuffleCharacterStats(plan, settings, tables)
    end
    if settings.gearStats then
        shuffleGearStats(plan, settings, tables)
    end
    if settings.gearPassives then
        shuffleGearPassives(plan, settings, tables)
    end
    if settings.weaknesses then
        shuffleResistances(plan, settings, tables, "ElementResistanceList", { [ENEMY_CATEGORY] = true, [BOSS_CATEGORY] = true },
            "weaknesses", "Enemy weaknesses", CHAOS_DATA.elements)
    end
    if settings.statusResists then
        shuffleResistances(plan, settings, tables, "StatusConditionResistanceList", { [ENEMY_CATEGORY] = true },
            "status resistances", "Status resistances", CHAOS_DATA.statuses)
    end
    if settings.enemyPassives then
        shuffleEnemyPassives(plan, settings, tables)
    end
    if settings.swapGear then
        swapGearTypes(plan, settings, tables)
    end
    if settings.encounterRate ~= "normal" then
        scaleEncounters(plan, settings, tables)
    end
    if NEEDED_BY.difficulty(settings) then
        scaleRewards(plan, settings, tables)
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
    if patch.mapKey ~= nil then
        local map = row[patch.field]
        if plainValue(map:Find(patch.mapKey)) == patch.value then
            return false
        end
        map:Add(patch.mapKey, patch.value)
        return true
    end
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
    local current = target[key]
    if current == patch.value or (type(current) == "number" and type(patch.value) == "number"
            and math.abs(current - patch.value) < 1e-4) then
        return false
    end
    target[key] = patch.value
    return true
end

local settings = readSettings()
local plan, index
local waitingFor
local reportedErrors = {}
-- What Status.txt reports: waiting, applied or error, the most field changes
-- one pass applied, how many fields failed, and the last error.
local status = { state = "waiting", applied = 0, failures = 0, message = "", time = os.date("%Y-%m-%d %H:%M") }

local function writeStatus()
    local file = io.open(STATUS_FILE, "w")
    if file == nil then
        return
    end
    local function clean(text) return (tostring(text):gsub("[\r\n]+", " ")) end
    file:write("# Written by the FFR Randomizer mod at each game start. The app reads it.\n")
    file:write("version=" .. MOD_VERSION .. "\n")
    file:write("seed=" .. clean(settings.seed) .. "\n")
    file:write("code=" .. clean(settingsCode(settings)) .. "\n")
    file:write("state=" .. status.state .. "\n")
    file:write("started=" .. status.time .. "\n")
    file:write("applied_at=" .. (status.appliedAt or "") .. "\n")
    file:write("planned=" .. (plan and #plan.patches or 0) .. "\n")
    file:write("changes=" .. status.applied .. "\n")
    file:write("errors=" .. status.failures .. "\n")
    file:write("check=" .. (plan and plan.check or "") .. "\n")
    file:write("message=" .. clean(status.message) .. "\n")
    file:close()
end

local function reportOnce(key, message)
    if not reportedErrors[key] then
        reportedErrors[key] = true
        log(message)
        return true
    end
    return false
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
                        if reportOnce(patch.family .. where, "Failed on " .. where .. ": " .. tostring(result)) then
                            status.failures = status.failures + 1
                            status.message = "Failed on " .. where .. ": " .. tostring(result)
                        end
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
        plan.check = seedCheck(plan, settings)
        log(string.format("Plan ready: %d changes for seed %s. Seed check: %s.", #plan.patches, settings.seed, plan.check))
        writeSpoiler(plan, settings)
        if unitNamesRead + unitNamesMissing > 0 then
            log(string.format("Unit names: %d read from the game, %d without a name.", unitNamesRead, unitNamesMissing))
        end
    end
    if index == nil then
        index = buildIndex(plan)
    end
    local failures = status.failures
    local changed = applyPlan()
    if changed > 0 then
        log(string.format("Applied %d field changes.", changed))
    end
    -- The first pass reports, and so does any later pass that changed more
    -- (a rebuilt table) or hit a new failure.
    if status.state ~= "applied" or changed > status.applied or status.failures ~= failures then
        status.state = "applied"
        status.applied = math.max(status.applied, changed)
        status.appliedAt = os.date("%Y-%m-%d %H:%M")
        writeStatus()
    end
end

log("Version " .. MOD_VERSION .. " loaded. Seed " .. settings.seed .. ". Waiting for game tables.")
writeStatus()

LoopAsync(CHECK_INTERVAL_MS, function()
    ExecuteInGameThread(function()
        local ok, err = pcall(tick)
        if not ok then
            if reportOnce("tick" .. tostring(err), "Error: " .. tostring(err)) and status.state ~= "applied" then
                status.state = "error"
                status.message = "Error: " .. tostring(err)
                writeStatus()
            end
        end
    end)
    return false
end)
