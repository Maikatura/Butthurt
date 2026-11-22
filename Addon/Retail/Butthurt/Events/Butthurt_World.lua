local _G = getfenv(0);
local LibStub = _G.LibStub;


local name = ... or "Butthurt";
---@class Butthurt
local Butthurt = LibStub("AceAddon-3.0"):GetAddon(name);
if not Butthurt then return; end

-- Everything here comes from plain game events, no combat log (addons can't read it since Midnight).
-- Values that can be "secret" in combat are only touched inside pcall, like in Butthurt_Combat.lua.


-- Combat state ------------------------------------------------------------------------------------

Butthurt:AddEvent("Enter combat", "PLAYER_REGEN_DISABLED", function()
    Butthurt:Debug("Entering combat")
    Butthurt:Triggered("ENTER_COMBAT")
end)

Butthurt:AddEvent("Leave combat", "PLAYER_REGEN_ENABLED", function()
    Butthurt:Debug("Leaving combat")
    Butthurt:Triggered("LEAVE_COMBAT")
end)


-- Crowd control (stuns, fears, silences…) -----------------------------------------------------------

Butthurt:AddEvent("Crowd controlled", "LOSS_OF_CONTROL_ADDED", function(self, unit)
    -- Newer clients pass the unit, older ones don't (and then it's always you).
    if not Butthurt:IsPlayerUnit(unit) then return; end
    Butthurt:Debug("Crowd controlled")
    Butthurt:Triggered("CROWD_CONTROL")
end)


-- Coming back to life (resurrected, or back from the spirit healer) --------------------------------

local lastRevive = 0

local function Revived()
    local ok, dead = pcall(UnitIsDeadOrGhost, "player")
    if not ok or dead then return; end

    -- PLAYER_ALIVE and PLAYER_UNGHOST can both fire for one revive.
    local now = GetTime()
    if now - lastRevive < 3 then return; end
    lastRevive = now

    Butthurt:Debug("Back to life")
    Butthurt:Triggered("REVIVE")
end

Butthurt:AddEvent("Revive", "PLAYER_ALIVE", Revived)
Butthurt:AddEvent("Unghost", "PLAYER_UNGHOST", Revived)


-- Progress -----------------------------------------------------------------------------------------

Butthurt:AddEvent("Quest turned in", "QUEST_TURNED_IN", function()
    Butthurt:Debug("Quest turned in")
    Butthurt:Triggered("QUEST_DONE")
end)


-- Group content -------------------------------------------------------------------------------------

Butthurt:AddEvent("Boss pulled", "ENCOUNTER_START", function()
    Butthurt:Debug("Boss pulled")
    Butthurt:Triggered("BOSS_PULL")
end)

Butthurt:AddEvent("Boss killed", "ENCOUNTER_END", function(self, encounterID, encounterName, difficultyID, groupSize, success)
    local ok, killed = pcall(function() return success == 1 or success == true end)
    if not ok or not killed then return; end
    Butthurt:Debug("Boss killed")
    Butthurt:Triggered("BOSS_KILL")
end)

Butthurt:AddEvent("Ready check", "READY_CHECK", function()
    Butthurt:Debug("Ready check")
    Butthurt:Triggered("READY_CHECK")
end)


-- PvP -----------------------------------------------------------------------------------------------

Butthurt:AddEvent("Honorable kill", "PLAYER_PVP_KILLS_CHANGED", function(self, unit)
    if not Butthurt:IsPlayerUnit(unit) then return; end
    Butthurt:Debug("Honorable kill")
    Butthurt:Triggered("PVP_KILL")
end)


-- Social --------------------------------------------------------------------------------------------

local function Whispered()
    Butthurt:Debug("Whisper received")
    Butthurt:Triggered("WHISPER")
end

Butthurt:AddEvent("Whisper", "CHAT_MSG_WHISPER", Whispered)
Butthurt:AddEvent("Battle.net whisper", "CHAT_MSG_BN_WHISPER", Whispered)
