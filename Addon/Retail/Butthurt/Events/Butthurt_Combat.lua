local _G = getfenv(0);
local LibStub = _G.LibStub;


local name = ... or "Butthurt";
---@class Butthurt
local Butthurt = LibStub("AceAddon-3.0"):GetAddon(name);
if not Butthurt then return; end


local function PlayerHit(self, unitTarget, eventType, flagText, amount, schoolMask)
    Butthurt:Debug("Dmg " .. tostring(amount) .. " (" .. tostring(eventType) .. ")")

    if eventType ~= "HEAL" and amount and amount > 0 then
        Butthurt:Triggered()
    end
end


-- Register the event
--Butthurt:RegisterEvent("UNIT_COMBAT", PlayerHit)


Butthurt:AddEvent("Combat", "UNIT_COMBAT", function(self, unitTarget, eventType, flagText, amount, schoolMask)
     PlayerHit(self, unitTarget, eventType, flagText, amount, schoolMask)
end)
