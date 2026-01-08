-- up-value the globals
local _G = getfenv(0);
local LibStub = _G.LibStub;

local name = ... or "Butthurt";
--- @class Butthurt: AceAddon,AceConsole-3.0,AceEvent-3.0,AceHook-3.0
local Butthurt = LibStub("AceAddon-3.0"):NewAddon(name, "AceConsole-3.0", "AceEvent-3.0", "AceHook-3.0");
if not Butthurt then return; end

local prototype = { OnEnable = function(self) self.OnEnable() end }
Butthurt:SetDefaultModulePrototype(prototype)  
Butthurt:SetDefaultModuleLibraries("AceEvent-3.0")

Butthurt.Events = Butthurt.Events or {};
Butthurt.pixelFlashFrame = CreateFrame("Frame", "PixelFlashFrame", UIParent)

function Butthurt:Debug(string)
    print("|cFFFF0000[Butthurt] " .."|cFFFFFFFF" .. string)
end

function Butthurt:GetPixelFlashFrame()
    return Butthurt.pixelFlashFrame
end

function Butthurt:Triggered()
    local frame = Butthurt.pixelFlashFrame
    if not frame then
        -- Create frame if missing
        frame = CreateFrame("Frame", "ButthurtPixelFlashFrame", UIParent)
        frame:SetSize(100, 100)
        frame:SetPoint("CENTER")
        frame.texture = frame:CreateTexture(nil, "OVERLAY")
        frame.texture:SetAllPoints()
        frame.texture:SetColorTexture(0, 0, 0, 1)
        frame:Hide()
        Butthurt.pixelFlashFrame = frame
    end

    local color = frame.selectedColor or {1, 0, 0, 1}
    frame.texture:SetColorTexture(unpack(color))
    frame:Show()

    local duration = frame.flashDuration or 0.2
    C_Timer.After(duration, function()
        frame.texture:SetColorTexture(0, 0, 0, 1)
        frame:Hide()
    end)
end


function Butthurt:AddEvent(callbackName, event, callback)
    table.insert(self.Events, {event = event, callback = callback, callbackName = callbackName})
end

function Butthurt:LoadAddEvents()
    for _, eventPair in ipairs(self.Events) do
        Butthurt:Debug("Loaded " .. eventPair.callbackName .. " with " .. eventPair.event)
        Butthurt:RegisterEvent(eventPair.event, eventPair.callback)
    end
end



function Butthurt:OnInitialize()
    self:Debug("Loading...")

    
    Butthurt.pixelFlashFrame:SetFrameStrata("HIGH")
    Butthurt.pixelFlashFrame:SetFrameStrata("HIGH")
    Butthurt.pixelFlashFrame:SetWidth(5)
    Butthurt.pixelFlashFrame:SetHeight(5)
    Butthurt.pixelFlashFrame:SetPoint("TOPLEFT", UIParent, "TOPLEFT")
    Butthurt.pixelFlashFrame.texture = Butthurt.pixelFlashFrame:CreateTexture(nil, "BACKGROUND")
    Butthurt.pixelFlashFrame.texture:SetAllPoints(Butthurt.pixelFlashFrame)
    Butthurt.pixelFlashFrame.texture:SetColorTexture(0, 0, 0, 1) -- Default to black

    self:LoadAddEvents()

    self:Debug("Loading complete")
end


-- -- Example usage
-- Butthurt:SubscribeEvent("COMBAT_LOG_EVENT_UNFILTERED", function(...)
--     print("Combat log event triggered with args:", ...)
-- end)

-- -- Simulate triggering the event

