local _G = getfenv(0);
local LibStub = _G.LibStub;


local name = ... or "Butthurt";
---@class Butthurt
local Butthurt = LibStub("AceAddon-3.0"):GetAddon(name);
if not Butthurt then return; end


-- Since Midnight some combat values can be "secret" and comparing them throws an error,
-- so every comparison is wrapped and falls back to treating the hit as damage.
local function IsHeal(eventType)
    local ok, result = pcall(function() return eventType == "HEAL" end)
    return ok and result
end

local function HasAmount(amount)
    local ok, result = pcall(function() return amount ~= nil and amount > 0 end)
    return not ok or result
end

local function PlayerHit(self, unitTarget, eventType, flagText, amount, schoolMask)
    -- UNIT_COMBAT fires for every unit with a token (target, party, ...), we only care about ourselves.
    if unitTarget ~= "player" then return; end

    if IsHeal(eventType) then
        Butthurt:Triggered("HEAL")
    elseif HasAmount(amount) then
        Butthurt:Debug("Got hit")
        Butthurt:Triggered("DAMAGE")
    end
end


Butthurt:AddEvent("Combat", "UNIT_COMBAT", function(self, unitTarget, eventType, flagText, amount, schoolMask)
     PlayerHit(self, unitTarget, eventType, flagText, amount, schoolMask)
end)
