-- FFRProbe: a test-only UE4SS mod for finding how chests and battles work.
-- Not shipped. It writes FFRProbe.txt next to this Scripts folder:
--   1. every class (native in /Script/mucha, or a Blueprint) whose name
--      mentions treasure boxes, gimmicks, encounters or battle managers, with
--      each function and its parameters;
--   2. the battle entry data the game hands CPP_MapManager:Encount for each
--      field battle, as a template for starting one ourselves;
--   3. every call to the functions listed under WATCH, so opening a chest
--      shows which of them run.

local DIR = debug.getinfo(1, "S").source:match("^@(.*[\\/])Scripts[\\/]main%.lua$") or "ue4ss/Mods/FFRProbe/"
local OUT = DIR .. "FFRProbe.txt"
local file = io.open(OUT, "w")

local function write(line)
    if file then
        file:write(line, "\n")
        file:flush()
    end
    print("[FFRProbe] " .. line .. "\n")
end

local PATTERNS = { "treasure", "gimmick", "encount", "mapmanager", "battlemanager", "battlesubsystem",
    "bfl_battle", "itemget", "obtain" }

local function wanted(name)
    local lower = name:lower()
    for _, p in ipairs(PATTERNS) do
        if lower:find(p, 1, true) then
            return true
        end
    end
    return false
end

local function describe(cls)
    local lines = {}
    pcall(function()
        cls:ForEachFunction(function(fn)
            local params = {}
            pcall(function()
                fn:ForEachProperty(function(prop)
                    params[#params + 1] = prop:GetClass():GetFName():ToString() .. " " .. prop:GetFName():ToString()
                end)
            end)
            lines[#lines + 1] = "    " .. fn:GetFName():ToString() .. "(" .. table.concat(params, ", ") .. ")"
        end)
    end)
    pcall(function()
        cls:ForEachProperty(function(prop)
            lines[#lines + 1] = "    field " .. prop:GetClass():GetFName():ToString() .. " " .. prop:GetFName():ToString()
        end)
    end)
    return lines
end

local dumped = false
local function dumpClasses()
    local found = 0
    ForEachUObject(function(object)
        pcall(function()
            local kind = object:GetClass():GetFName():ToString()
            if kind == "Class" or kind == "BlueprintGeneratedClass" or kind == "ScriptStruct" then
                local full = object:GetFullName()
                local isGame = full:find("/Script/mucha", 1, true) or full:find("/Game/", 1, true)
                if isGame and wanted(object:GetFName():ToString()) then
                    found = found + 1
                    write(full)
                    for _, line in ipairs(describe(object)) do
                        write(line)
                    end
                end
            end
        end)
    end)
    write(string.format("== %d classes and structs listed ==", found))
end

-- Field battles: the entry the game builds, field by field.
local ENTRY_FIELDS = { "friendliesUnitIdList", "friendliesLocationIdList", "enemiesUnitIdList", "enemiesLocationIdList",
    "battleGroupId", "battleStageId", "battleEventId", "bgmCueName", "CanEscape", "regionId", "isBounty" }

local function show(value)
    if type(value) ~= "userdata" then
        return tostring(value)
    end
    local text
    if pcall(function() text = value:ToString() end) and text then
        return text
    end
    local items = {}
    if pcall(function() value:ForEach(function(_, e) items[#items + 1] = tostring(e:get()) end) end) then
        return "[" .. table.concat(items, ",") .. "]"
    end
    return tostring(value)
end

local function hookEncount()
    local ok, err = pcall(function()
        RegisterHook("/Script/mucha.CPP_MapManager:Encount", function(_, entry, isEvent)
            pcall(function()
                local e = entry:get()
                write("-- Encount, isEvent " .. tostring(isEvent:get()))
                for _, f in ipairs(ENTRY_FIELDS) do
                    local v
                    pcall(function() v = e[f] end)
                    write("   " .. f .. " = " .. show(v))
                end
            end)
        end)
    end)
    write(ok and "Hooked CPP_MapManager:Encount" or ("Encount hook failed: " .. tostring(err)))
end

-- Functions to watch while a chest opens. Filled in from the class list
-- after the first run; each hook just logs the call.
local WATCH = {
}

local function hookWatch()
    for _, path in ipairs(WATCH) do
        local ok, err = pcall(function()
            RegisterHook(path, function() write("CALL " .. path) end)
        end)
        write((ok and "Watching " or "Can't watch ") .. path .. (ok and "" or (": " .. tostring(err))))
    end
end

-- Every field of a struct value, its parent structs' fields included, and
-- nested structs down to `depth` levels.
local function dumpStruct(struct, value, indent, depth, out)
    local chain = {}
    pcall(function()
        local s = struct
        while s ~= nil and s:IsValid() do
            table.insert(chain, 1, s)
            s = s:GetSuperStruct()
        end
    end)
    for _, s in ipairs(chain) do
        pcall(function()
            s:ForEachProperty(function(inner)
                local name = inner:GetFName():ToString()
                local kind = inner:GetClass():GetFName():ToString()
                local v
                pcall(function() v = value[name] end)
                if kind == "StructProperty" and depth > 0 then
                    local child
                    pcall(function() child = inner:GetStruct() end)
                    out[#out + 1] = indent .. kind .. " " .. name .. " : " .. (child and child:GetFName():ToString() or "?")
                    if child then
                        dumpStruct(child, v, indent .. "   ", depth - 1, out)
                    end
                else
                    out[#out + 1] = indent .. kind .. " " .. name .. " = " .. show(v)
                end
            end)
        end)
    end
end

local function structFields(object, fieldName)
    local out = {}
    pcall(function()
        local cls = object:GetClass()
        while cls ~= nil and cls:IsValid() and #out == 0 do
            cls:ForEachProperty(function(prop)
                if #out == 0 and prop:GetFName():ToString() == fieldName then
                    local struct = prop:GetStruct()
                    out[#out + 1] = fieldName .. " : " .. struct:GetFName():ToString()
                    dumpStruct(struct, object[fieldName], "      ", 3, out)
                end
            end)
            cls = cls:GetSuperStruct()
        end
    end)
    return out
end

local function structFieldsOld(object, fieldName)
    local out = {}
    pcall(function()
        object:GetClass():ForEachProperty(function(prop)
            if prop:GetFName():ToString() == fieldName then
                local value = object[fieldName]
                local struct = prop:GetStruct()
                out[#out + 1] = fieldName .. " : " .. struct:GetFName():ToString()
                struct:ForEachProperty(function(inner)
                    local name = inner:GetFName():ToString()
                    local v
                    pcall(function() v = value[name] end)
                    out[#out + 1] = "      " .. inner:GetClass():GetFName():ToString() .. " " .. name .. " = " .. show(v)
                end)
            end
        end)
    end)
    -- Fields inherited from a parent class aren't in GetClass()'s own list.
    if #out == 0 then
        pcall(function()
            local cls = object:GetClass()
            while cls ~= nil and cls:IsValid() and #out == 0 do
                cls:ForEachProperty(function(prop)
                    if prop:GetFName():ToString() == fieldName then
                        local value = object[fieldName]
                        local struct = prop:GetStruct()
                        out[#out + 1] = fieldName .. " : " .. struct:GetFName():ToString()
                        struct:ForEachProperty(function(inner)
                            local name = inner:GetFName():ToString()
                            local v
                            pcall(function() v = value[name] end)
                            out[#out + 1] = "      " .. inner:GetClass():GetFName():ToString() .. " " .. name .. " = " .. show(v)
                        end)
                    end
                end)
                cls = cls:GetSuperStruct()
            end
        end)
    end
    return out
end

-- Live chests in the current map: their id, table data and both event structs.
local function dumpChests()
    local chests = {}
    pcall(function() chests = FindAllOf("CPP_MapGimmick_TreasureBox") or {} end)
    write(string.format("== %d chests in this map ==", #chests))
    for i, chest in ipairs(chests) do
        if i > 2 then
            break
        end
        pcall(function()
            write("Chest " .. chest:GetFullName())
            local id, open = "?", "?"
            pcall(function() id = tostring(chest.m_Id) end)
            pcall(function() open = tostring(chest.m_IsOpen) end)
            write("   m_Id = " .. id .. ", m_IsOpen = " .. open)
            for _, field in ipairs({ "m_TreasureBoxData", "m_EventData", "m_TrapEventData" }) do
                for _, line in ipairs(structFields(chest, field)) do
                    write("   " .. line)
                end
            end
        end)
    end
end

write("FFRProbe started. Waiting for the map manager.")
LoopAsync(3000, function()
    if dumped then
        return true
    end
    local ready = false
    pcall(function()
        local manager = FindFirstOf("CPP_MapManager")
        ready = manager ~= nil and manager:IsValid()
    end)
    if not ready then
        return false
    end
    dumped = true
    ExecuteInGameThread(function()
        dumpChests()
        hookWatch()
        write("Ready. Walk into a map with chests and press F7 to list them again.")
    end)
    return true
end)

-- Probe 4: arm every unopened chest in this map as a trap whose trap event
-- is a new talk event row naming battle group 1000 (Steel Bats). F8 clones
-- the chest's own TreasureBox event for it, F9 the battle event
-- SymbolEncount_01, in case only cutscene events start battles.
local EVENT_TABLES = {
    "/Game/Datatable/Event/TalkEvent/CDT_TalkEventData_Prod.CDT_TalkEventData_Prod",
    "/Game/Datatable/Event/TalkEvent/DT_TalkEventData_Other.DT_TalkEventData_Other",
}
local TRAP_GROUP = 1000

local function armTraps(source, rowName)
    local made = 0
    for _, path in ipairs(EVENT_TABLES) do
        local ok, err = pcall(function()
            local dt = StaticFindObject(path)
            if dt == nil or not dt:IsValid() then
                error("table not loaded")
            end
            local original = dt:FindRow(source)
            if original == nil then
                -- SymbolEncount_01 lives in the combined table only.
                local combined = StaticFindObject(EVENT_TABLES[1])
                original = combined and combined:FindRow(source)
            end
            if original == nil then
                error(source .. " not found")
            end
            if dt:FindRow(rowName) == nil then
                dt:AddRow(rowName, original)
            end
            local row = dt:FindRow(rowName)
            row.encountGroupId = TRAP_GROUP
            write(path .. ": row " .. rowName .. " encountGroupId = " .. tostring(dt:FindRow(rowName).encountGroupId))
            made = made + 1
        end)
        if not ok then
            write(path .. ": " .. tostring(err))
        end
    end
    local chests = {}
    pcall(function() chests = FindAllOf("CPP_MapGimmick_TreasureBox") or {} end)
    local armed = 0
    for _, chest in ipairs(chests) do
        pcall(function()
            if not chest.m_IsOpen then
                chest.m_TreasureBoxData.IsTrap = true
                chest.m_TreasureBoxData.battleGroupId = TRAP_GROUP
                chest.m_TrapEventData.EventId = FName(rowName)
                armed = armed + 1
                write(string.format("Armed chest %s: IsTrap %s, trap event %s", tostring(chest.m_Id),
                    tostring(chest.m_TreasureBoxData.IsTrap), chest.m_TrapEventData.EventId:ToString()))
            end
        end)
    end
    write(string.format("Probe 4 (%s): %d event rows, %d chests armed with battle %d. Open one now.",
        source, made, armed, TRAP_GROUP))
end

pcall(function()
    RegisterKeyBind(Key.F8, function()
        ExecuteInGameThread(function() armTraps("TreasureBox", "FFRTrap_Box") end)
    end)
    RegisterKeyBind(Key.F9, function()
        ExecuteInGameThread(function() armTraps("SymbolEncount_01", "FFRTrap_Seq") end)
    end)
end)

-- F7 lists the chests of whatever map you're in.
pcall(function()
    RegisterKeyBind(Key.F7, function()
        ExecuteInGameThread(dumpChests)
    end)
end)
