-- FFRMusicProbe: a test-only UE4SS mod for the music randomizer. Not shipped.
-- Writes FFRMusicProbe.txt next to this Scripts folder:
--   1. every field of every map's bgmList (CDT_MapData_Prod), since the
--      table export left that list out;
--   2. every battle group's bgmCueName;
--   3. every distinct BGM cue name found in both.
-- Ctrl+F6 swaps every map and battle cue to TEST_CUE (the final boss theme)
-- and back again, to prove a changed cue plays in the field and in battle.

local DIR = debug.getinfo(1, "S").source:match("^@(.*[\\/])Scripts[\\/]main%.lua$") or "ue4ss/Mods/FFRMusicProbe/"
local OUT = DIR .. "FFRMusicProbe.txt"
local file = io.open(OUT, "w")

local MAPS = "/Game/Datatable/Map/CDT_MapData_Prod.CDT_MapData_Prod"
local GROUPS = "/Game/Datatable/Encount/DT_BattleGroup.DT_BattleGroup"
local TEST_CUE = "BGM_PP_BATTLE_LASTBOSS"

local function write(line)
    if file then
        file:write(line, "\n")
        file:flush()
    end
    print("[FFRMusicProbe] " .. line .. "\n")
end

local function text(value)
    if type(value) ~= "userdata" then
        return tostring(value)
    end
    local s
    if pcall(function() s = value:ToString() end) and s then
        return s
    end
    return tostring(value)
end

local function findStruct(name)
    local found
    for _, path in ipairs({ "/Script/mucha." .. name, "/Script/mucha.F" .. name }) do
        pcall(function()
            local s = StaticFindObject(path)
            if s ~= nil and s:IsValid() then
                found = found or s
            end
        end)
    end
    if found then
        return found
    end
    ForEachUObject(function(object)
        if found then
            return
        end
        pcall(function()
            if object:GetClass():GetFName():ToString() == "ScriptStruct" and object:GetFName():ToString() == name then
                found = object
            end
        end)
    end)
    return found
end

-- { name, kind } for every field of a struct, parents first.
local function fieldsOf(struct)
    local chain, fields = {}, {}
    pcall(function()
        local s = struct
        while s ~= nil and s:IsValid() do
            table.insert(chain, 1, s)
            s = s:GetSuperStruct()
        end
    end)
    for _, s in ipairs(chain) do
        pcall(function()
            s:ForEachProperty(function(prop)
                fields[#fields + 1] = { name = prop:GetFName():ToString(), kind = prop:GetClass():GetFName():ToString() }
            end)
        end)
    end
    return fields
end

local cues = {}
local cueOrder = {}
local function noteCue(name)
    if name and name ~= "" and not cues[name] then
        cues[name] = 0
        cueOrder[#cueOrder + 1] = name
    end
    if name and cues[name] then
        cues[name] = cues[name] + 1
    end
end

-- Every string or name field in a map's bgmList entries, so Ctrl+F6 can
-- swap them without knowing the struct's layout in advance.
local mapCueSlots = {}
local groupSlots = {}

local function dumpMaps()
    local dt = StaticFindObject(MAPS)
    if dt == nil or not dt:IsValid() then
        write("Map table not loaded")
        return
    end
    local struct = findStruct("_MAP_BGM_DATA")
    if struct == nil then
        write("_MAP_BGM_DATA struct not found")
        return
    end
    local fields = fieldsOf(struct)
    local names = {}
    for _, f in ipairs(fields) do
        names[#names + 1] = f.kind .. " " .. f.name
    end
    write("_MAP_BGM_DATA: " .. struct:GetFullName())
    write("   fields: " .. table.concat(names, ", "))
    local maps, entries = 0, 0
    dt:ForEachRow(function(rowName, row)
        maps = maps + 1
        local list = row.bgmList
        local count = 0
        pcall(function() count = #list end)
        if count == 0 then
            return
        end
        write(string.format("Map %s (ID %s): %d entries", text(rowName), text(row.ID), count))
        for i = 1, count do
            entries = entries + 1
            local e = list[i]
            local parts = {}
            for _, f in ipairs(fields) do
                local v
                pcall(function() v = e[f.name] end)
                local s = text(v)
                parts[#parts + 1] = f.name .. "=" .. s
                if f.kind == "StrProperty" or f.kind == "NameProperty" then
                    if s:find("^BGM_") then
                        noteCue(s)
                        mapCueSlots[#mapCueSlots + 1] = { entry = e, field = f.name, kind = f.kind, original = s }
                    end
                end
            end
            write("   [" .. i .. "] " .. table.concat(parts, ", "))
        end
    end)
    write(string.format("== %d maps, %d bgmList entries, %d cue fields ==", maps, entries, #mapCueSlots))
end

local function dumpGroups()
    local dt = StaticFindObject(GROUPS)
    if dt == nil or not dt:IsValid() then
        write("Battle group table not loaded")
        return
    end
    local n = 0
    dt:ForEachRow(function(rowName, row)
        local s = text(row.bgmCueName)
        n = n + 1
        noteCue(s)
        groupSlots[#groupSlots + 1] = { row = row, original = s }
    end)
    write(string.format("== %d battle groups read ==", n))
end

local function setField(holder, field, kind, value)
    if kind == "NameProperty" then
        holder[field] = FName(value)
    else
        holder[field] = value
    end
end

local swapped = false
local function toggleSwap()
    swapped = not swapped
    local okMaps, okGroups, failed = 0, 0, 0
    for _, slot in ipairs(mapCueSlots) do
        local ok = pcall(setField, slot.entry, slot.field, slot.kind, swapped and TEST_CUE or slot.original)
        if ok then okMaps = okMaps + 1 else failed = failed + 1 end
    end
    for _, slot in ipairs(groupSlots) do
        local ok = pcall(setField, slot.row, "bgmCueName", "StrProperty", swapped and TEST_CUE or slot.original)
        if ok then okGroups = okGroups + 1 else failed = failed + 1 end
    end
    local readMap, readGroup = "?", "?"
    pcall(function() readMap = text(mapCueSlots[1].entry[mapCueSlots[1].field]) end)
    pcall(function() readGroup = text(groupSlots[1].row.bgmCueName) end)
    write(string.format("Ctrl+F6: music %s. %d map fields, %d battle groups set, %d failed. Read back: map %s, battle %s",
        swapped and ("swapped to " .. TEST_CUE) or "restored", okMaps, okGroups, failed, readMap, readGroup))
    write(swapped and "Now leave this area and come back, then get into a random battle." or "Music restored.")
end

-- Run 2: swap as soon as the tables load, before any save, like the real
-- mod, to rule out the game caching an already loaded town's music.
write("FFRMusicProbe started. Waiting for the map and battle group tables.")
local done = false
LoopAsync(1000, function()
    if done then
        return true
    end
    local ready = false
    pcall(function()
        local maps, groups = StaticFindObject(MAPS), StaticFindObject(GROUPS)
        ready = maps ~= nil and maps:IsValid() and groups ~= nil and groups:IsValid()
    end)
    if not ready then
        return false
    end
    done = true
    ExecuteInGameThread(function()
        pcall(dumpMaps)
        pcall(dumpGroups)
        write("== Cue names (times used) ==")
        for _, name in ipairs(cueOrder) do
            write(string.format("   %s  %d", name, cues[name]))
        end
        toggleSwap()
        write("Swapped at load. Ctrl+F6 restores, again swaps.")
    end)
    return true
end)

-- Run 3: vehicle music isn't in bgmList. Ctrl+F5 dumps the map manager's
-- music controller (fields with values, functions with parameters) and any
-- loaded object whose class mentions bgm, sound control, vehicle, chocobo or
-- airship, listing string and name values. Press it while riding.
local function propsOf(cls)
    local list = {}
    pcall(function()
        local c = cls
        while c ~= nil and c:IsValid() do
            c:ForEachProperty(function(p)
                list[#list + 1] = p
            end)
            c = c:GetSuperStruct()
        end
    end)
    return list
end

local function valueLine(holder, prop, indent, depth, out)
    local name = prop:GetFName():ToString()
    local kind = prop:GetClass():GetFName():ToString()
    local v
    pcall(function() v = holder[name] end)
    if kind == "StructProperty" and depth > 0 and v ~= nil then
        local s
        pcall(function() s = prop:GetStruct() end)
        out[#out + 1] = indent .. kind .. " " .. name .. " : " .. (s and s:GetFName():ToString() or "?")
        if s then
            for _, inner in ipairs(propsOf(s)) do
                valueLine(v, inner, indent .. "   ", depth - 1, out)
            end
        end
    elseif kind == "ArrayProperty" and v ~= nil then
        local items, n = {}, 0
        pcall(function() n = #v end)
        for i = 1, math.min(n, 20) do
            local e
            pcall(function() e = v[i] end)
            local t = text(e)
            if type(e) == "userdata" then
                -- Struct elements: list their string-ish fields.
                local inner
                pcall(function() inner = prop:GetInner() end)
                local s
                pcall(function() s = inner:GetStruct() end)
                if s then
                    local parts = {}
                    for _, f in ipairs(propsOf(s)) do
                        local fv
                        pcall(function() fv = e[f:GetFName():ToString()] end)
                        parts[#parts + 1] = f:GetFName():ToString() .. "=" .. text(fv)
                    end
                    t = "{" .. table.concat(parts, ", ") .. "}"
                end
            end
            items[#items + 1] = t
        end
        out[#out + 1] = indent .. kind .. " " .. name .. " (" .. n .. ") = [" .. table.concat(items, "; ") .. "]"
    else
        out[#out + 1] = indent .. kind .. " " .. name .. " = " .. text(v)
    end
end

local function dumpObject(object, label)
    local out = {}
    pcall(function()
        local cls = object:GetClass()
        out[#out + 1] = label .. ": " .. object:GetFullName()
        pcall(function()
            local c = cls
            while c ~= nil and c:IsValid() do
                c:ForEachFunction(function(fn)
                    local params = {}
                    pcall(function()
                        fn:ForEachProperty(function(p)
                            params[#params + 1] = p:GetClass():GetFName():ToString() .. " " .. p:GetFName():ToString()
                        end)
                    end)
                    out[#out + 1] = "   fn " .. c:GetFName():ToString() .. ":" .. fn:GetFName():ToString() .. "(" .. table.concat(params, ", ") .. ")"
                end)
                c = c:GetSuperStruct()
            end
        end)
        for _, p in ipairs(propsOf(cls)) do
            valueLine(object, p, "   ", 2, out)
        end
    end)
    for _, line in ipairs(out) do
        write(line)
    end
end

local VEHICLE_PATTERNS = { "bgm", "soundctrl", "soundcontrol", "vehicle", "chocobo", "airship", "airshipe" }

local function dumpVehicleMusic()
    write("== Ctrl+F5 dump ==")
    pcall(function()
        local manager = FindFirstOf("CPP_MapManager")
        write("Map manager m_BGMId = " .. text(manager.m_BGMId))
        local ctrl = manager.m_pMapBgmControlle
        if ctrl ~= nil and ctrl:IsValid() then
            dumpObject(ctrl, "Music controller")
        else
            write("No music controller on the map manager")
        end
    end)
    local seen, n = {}, 0
    ForEachUObject(function(object)
        if n >= 40 then
            return
        end
        pcall(function()
            local cls = object:GetClass()
            local cname = cls:GetFName():ToString():lower()
            local kind = cls:GetClass():GetFName():ToString()
            if kind ~= "Class" and kind ~= "BlueprintGeneratedClass" then
                return
            end
            local full = object:GetFullName()
            for _, p in ipairs(VEHICLE_PATTERNS) do
                if cname:find(p, 1, true) and not seen[full] then
                    seen[full] = true
                    n = n + 1
                    dumpObject(object, "Object")
                    return
                end
            end
        end)
    end)
    write(string.format("== %d matching objects dumped ==", n))
end

-- Run 4: Ctrl+F4 scans every loaded object for string fields (top level and
-- one or two structs deep) holding a BGM_ cue, so the airship and chocobo
-- cues turn up wherever they live. String field lists are cached per class.
local function stringPaths(struct, prefix, depth, out)
    for _, p in ipairs(propsOf(struct)) do
        local name = p:GetFName():ToString()
        local kind = p:GetClass():GetFName():ToString()
        if kind == "StrProperty" or kind == "NameProperty" then
            out[#out + 1] = { prefix .. name }
        elseif kind == "StructProperty" and depth > 0 then
            local s
            pcall(function() s = p:GetStruct() end)
            if s then
                local inner = {}
                stringPaths(s, "", depth - 1, inner)
                for _, path in ipairs(inner) do
                    local full = { name }
                    for _, part in ipairs(path) do
                        full[#full + 1] = part
                    end
                    out[#out + 1] = full
                end
            end
        end
    end
    return out
end

local function scanCues()
    write("== Ctrl+F4 scan for BGM_ strings ==")
    local cache, objects, hits = {}, 0, 0
    ForEachUObject(function(object)
        objects = objects + 1
        pcall(function()
            local cls = object:GetClass()
            local key = cls:GetFullName()
            local paths = cache[key]
            if paths == nil then
                paths = {}
                pcall(stringPaths, cls, "", 2, paths)
                -- stringPaths gives single-name tables for top level fields.
                for i, p in ipairs(paths) do
                    if type(p) ~= "table" then
                        paths[i] = { p }
                    end
                end
                cache[key] = paths
            end
            for _, path in ipairs(paths) do
                local v = object
                local ok = pcall(function()
                    for _, part in ipairs(path) do
                        v = v[part]
                    end
                end)
                if ok and v ~= nil then
                    local s = text(v)
                    if s:find("BGM_", 1, true) then
                        hits = hits + 1
                        write(string.format("   %s . %s = %s", object:GetFullName(), table.concat(path, "."), s))
                    end
                end
            end
        end)
    end)
    write(string.format("== %d objects scanned, %d BGM_ strings ==", objects, hits))
end

-- Run 5: vehicle cue names are hard-coded. Ctrl+F3 lists every game or
-- CRI function whose name mentions bgm or sound play, and hooks each to log
-- its calls with their arguments, so boarding the airship shows the call
-- that starts its music (if it goes through a hookable function).
local hooked = false
local function hookBgmFunctions()
    if hooked then
        write("Ctrl+F3: hooks already in place")
        return
    end
    hooked = true
    local paths = {}
    ForEachUObject(function(object)
        pcall(function()
            local kind = object:GetClass():GetFName():ToString()
            if kind ~= "Function" then
                return
            end
            local full = object:GetFullName()
            local path = full:match("^Function (.+)$")
            if path == nil then
                return
            end
            if not (path:find("/Script/mucha", 1, true) or path:find("/Script/CriWare", 1, true) or path:find("/Game/", 1, true)) then
                return
            end
            local fname = object:GetFName():ToString():lower()
            if fname:find("bgm", 1, true) or (fname:find("play", 1, true) and (fname:find("sound", 1, true) or fname:find("cue", 1, true))) then
                local params = {}
                pcall(function()
                    object:ForEachProperty(function(p)
                        params[#params + 1] = p:GetClass():GetFName():ToString() .. " " .. p:GetFName():ToString()
                    end)
                end)
                paths[#paths + 1] = path
                write("   fn " .. path .. "(" .. table.concat(params, ", ") .. ")")
            end
        end)
    end)
    local ok = 0
    for _, path in ipairs(paths) do
        local good = pcall(function()
            RegisterHook(path, function(self, ...)
                local args = {}
                for i, a in ipairs({ ... }) do
                    local v
                    pcall(function() v = a:get() end)
                    args[#args + 1] = text(v)
                end
                write("CALL " .. path .. "(" .. table.concat(args, ", ") .. ")")
            end)
        end)
        if good then
            ok = ok + 1
        end
    end
    write(string.format("Ctrl+F3: %d functions found, %d hooked. Board the airship or a chocobo now.", #paths, ok))
end

pcall(function()
    RegisterKeyBind(Key.F3, { ModifierKey.CONTROL }, function()
        ExecuteInGameThread(hookBgmFunctions)
    end)
end)

pcall(function()
    RegisterKeyBind(Key.F4, { ModifierKey.CONTROL }, function()
        ExecuteInGameThread(scanCues)
    end)
end)

pcall(function()
    RegisterKeyBind(Key.F5, { ModifierKey.CONTROL }, function()
        ExecuteInGameThread(dumpVehicleMusic)
    end)
end)

pcall(function()
    RegisterKeyBind(Key.F6, { ModifierKey.CONTROL }, function()
        ExecuteInGameThread(toggleSwap)
    end)
end)
