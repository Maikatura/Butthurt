local _G = getfenv(0);
local LibStub = _G.LibStub;


local name = ... or "Butthurt";
---@class Butthurt
local Butthurt = LibStub("AceAddon-3.0"):GetAddon(name);
if not Butthurt then return; end


local function CombatEvent(self, event)
    local timestamp, subEvent, hideCaster, sourceGUID, sourceName, sourceFlags, sourceRaidFlags, destGUID, destName, destFlags, destRaidFlags, spellId, spellName, spellSchool, amount, overkill, school, resisted, blocked, absorbed, critical, glancing, crushing, isOffHand, multistrike =CombatLogGetCurrentEventInfo()

    if subEvent == "SWING_DAMAGE" or subEvent == "RANGE_DAMAGE" or subEvent == "SPELL_DAMAGE" then
        if destGUID == UnitGUID("player") then
            Butthurt:Triggered()
        end
    end
end

Butthurt:AddEvent("Combat", "COMBAT_LOG_EVENT_UNFILTERED", function(self, event)
     CombatEvent(self, event)
end)