local Live = {}
Live.__index = Live

local function read(path)
    local file = io.open(path, "rb")
    if not file then return nil end
    local text = file:read("*a"); file:close(); return text
end

local function write(path, text)
    local file, err = io.open(path, "wb")
    if not file then error(err, 0) end
    file:write(text); file:close()
end

function Live.new(apply, directory, status)
    if not directory then
        local source = debug.getinfo(1, "S").source:gsub("^@", "")
        directory = source:match("^(.*[/\\])") .. "../"
    end
    return setmetatable({directory=directory, apply=apply, status=status, last_id=read(directory .. "live-request.id"), next_poll=0}, Live)
end

function Live:publish()
    local state, detail = "ready", ""
    if self.status then state, detail = self.status() end
    write(self.directory .. "live-status.txt", "PBZ-LIVE-1\n" .. tostring(os.time()) .. "\n" .. state .. "\n" .. (detail or ""))
end

function Live:poll(now)
    if now < self.next_poll then return end
    self.next_poll = now + .05
    if not self.next_publish or now >= self.next_publish then
        self:publish()
        self.next_publish = now + 1
    end
    local id = read(self.directory .. "live-request.id")
    if not id or id == self.last_id then return end
    self.last_id = id
    local ok, err = pcall(function()
        local chunk, load_error = loadfile(self.directory .. "live-request.lua")
        if not chunk then error(load_error, 0) end
        local request = chunk()
        if request.id ~= id then error("The live request changed while loading", 0) end
        local applied, reason = self.apply(request.configuration)
        if not applied then error(reason, 0) end
    end)
    write(self.directory .. "live-response.txt", id .. "\n" .. (ok and "ok" or "error") .. "\n" .. (ok and "" or tostring(err)))
    self:publish()
end

return Live
