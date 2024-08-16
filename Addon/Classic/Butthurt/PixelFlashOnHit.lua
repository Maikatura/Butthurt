local frame = CreateFrame("Frame")
local pixelFlashFrame = CreateFrame("Frame", "PixelFlashFrame", UIParent)
pixelFlashFrame:SetFrameStrata("HIGH")
pixelFlashFrame:SetWidth(10)
pixelFlashFrame:SetHeight(10)
pixelFlashFrame:SetPoint("TOPLEFT", UIParent, "TOPLEFT")
pixelFlashFrame.texture = pixelFlashFrame:CreateTexture(nil, "BACKGROUND")
pixelFlashFrame.texture:SetColorTexture(0, 0, 0, 1) -- Initial color black
pixelFlashFrame.texture:SetAllPoints(pixelFlashFrame)

local function OnEvent(self, event, ...)
    if event == "COMBAT_LOG_EVENT_UNFILTERED" then
        local _, subEvent, _, _, _, _, _, destGUID = CombatLogGetCurrentEventInfo()
        if subEvent == "SWING_DAMAGE" or subEvent == "RANGE_DAMAGE" or subEvent == "SPELL_DAMAGE" then
            if destGUID == UnitGUID("player") then
                pixelFlashFrame.texture:SetColorTexture(1, 0, 0, 1) -- Red color
                C_Timer.After(0.2, function() pixelFlashFrame.texture:SetColorTexture(0, 0, 0, 1) end) -- Back to black after 0.2 seconds
            end
        end
    end
end

frame:RegisterEvent("COMBAT_LOG_EVENT_UNFILTERED")
frame:SetScript("OnEvent", OnEvent)
