local _G = getfenv(0);
local LibStub = _G.LibStub;


local name = ... or "Butthurt";
---@class Butthurt
local Butthurt = LibStub("AceAddon-3.0"):GetAddon(name);
if not Butthurt then return; end


Butthurt:AddEvent("Death", "PLAYER_DEAD", function()
    Butthurt:Debug("You died")
    Butthurt:Triggered("DEATH")
end)

Butthurt:AddEvent("Level up", "PLAYER_LEVEL_UP", function()
    Butthurt:Debug("Level up")
    Butthurt:Triggered("LEVEL_UP")
end)

Butthurt:AddEvent("Achievement", "ACHIEVEMENT_EARNED", function()
    Butthurt:Debug("Achievement earned")
    Butthurt:Triggered("ACHIEVEMENT")
end)
