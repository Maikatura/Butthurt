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

-- Colors the Butthurt desktop app looks for in the top-left corner of the game window.
-- Keep these in sync with WowEvents.cs in the desktop app.
Butthurt.Colors = {
    IDLE        = { 0, 0, 0, 1 },
    DAMAGE      = { 1, 0, 0, 1 },
    HEAL        = { 0, 1, 0, 1 },
    LEVEL_UP    = { 0, 0, 1, 1 },
    DEATH       = { 1, 0, 1, 1 },
    ACHIEVEMENT = { 1, 1, 0, 1 },
};

-- Shown in the settings window, in this order.
Butthurt.EventInfo = {
    { key = "DAMAGE",      label = "Taking damage" },
    { key = "HEAL",        label = "Getting healed" },
    { key = "DEATH",       label = "Dying" },
    { key = "LEVEL_UP",    label = "Level up" },
    { key = "ACHIEVEMENT", label = "Achievement earned" },
};

local defaults = {
    enabled = true,
    debug = false,
    flashDuration = 0.2,
    events = {},
};

-- The color the corner pixel shows right now, read by the settings window.
Butthurt.currentColor = "IDLE"

-- Parented to nothing so the pixel survives Alt+Z (hidden UI) and ignores the UI scale.
Butthurt.pixelFlashFrame = CreateFrame("Frame", "ButthurtPixelFlashFrame", nil)

function Butthurt:Print(string)
    print("|cFFFF4D8D[Butthurt]|r " .. string)
end

function Butthurt:Debug(string)
    if ButthurtDB and ButthurtDB.debug then
        print("|cFFFF0000[Butthurt]|r |cFF999999" .. string .. "|r")
    end
end

function Butthurt:GetPixelFlashFrame()
    return Butthurt.pixelFlashFrame
end

function Butthurt:IsEventEnabled(colorName)
    return ButthurtDB.events[colorName] ~= false
end

function Butthurt:SetEventEnabled(colorName, enabled)
    ButthurtDB.events[colorName] = enabled
end

--- Flash the corner pixel with the color of the given event (defaults to DAMAGE).
--- force skips the on/off checks, used by the test buttons.
function Butthurt:Triggered(colorName, force)
    colorName = colorName or "DAMAGE"
    if not force and (not ButthurtDB.enabled or not self:IsEventEnabled(colorName)) then return; end

    local frame = Butthurt.pixelFlashFrame
    local color = Butthurt.Colors[colorName] or Butthurt.Colors.DAMAGE
    frame.texture:SetColorTexture(unpack(color))
    Butthurt.currentColor = colorName

    -- Only the most recent flash is allowed to reset the pixel, so rapid hits keep it lit.
    frame.flashId = (frame.flashId or 0) + 1
    local flashId = frame.flashId
    C_Timer.After(ButthurtDB.flashDuration or defaults.flashDuration, function()
        if frame.flashId == flashId then
            frame.texture:SetColorTexture(unpack(Butthurt.Colors.IDLE))
            Butthurt.currentColor = "IDLE"
        end
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

local function SetupPixelFrame()
    local frame = Butthurt.pixelFlashFrame
    frame:SetFrameStrata("TOOLTIP")
    frame:SetFrameLevel(10000)
    frame:SetSize(8, 8)
    frame:SetPoint("TOPLEFT", UIParent, "TOPLEFT")
    frame:SetIgnoreParentScale(true)
    frame:SetScale(1)
    frame.texture = frame:CreateTexture(nil, "OVERLAY")
    frame.texture:SetAllPoints(frame)
    frame.texture:SetColorTexture(unpack(Butthurt.Colors.IDLE))
    frame:Show()
end

function Butthurt:SlashCommand(input)
    local command, arg = self:GetArgs(input, 2)
    command = command and command:lower() or ""

    if command == "" or command == "show" or command == "config" then
        self:ToggleWindow()
    elseif command == "toggle" then
        ButthurtDB.enabled = not ButthurtDB.enabled
        self:Print("Haptics " .. (ButthurtDB.enabled and "|cFF22C55Eenabled|r" or "|cFFEF4444disabled|r"))
        self:RefreshWindow()
    elseif command == "test" then
        local colorName = (arg or "DAMAGE"):upper()
        if not Butthurt.Colors[colorName] or colorName == "IDLE" then
            self:Print("Unknown event. Try: damage, heal, death, level_up, achievement")
            return
        end
        self:Print("Testing " .. colorName)
        self:Triggered(colorName, true)
    elseif command == "debug" then
        ButthurtDB.debug = not ButthurtDB.debug
        self:Print("Debug " .. (ButthurtDB.debug and "on" or "off"))
    else
        self:Print("Commands:")
        self:Print("  /butthurt - open the settings window")
        self:Print("  /butthurt toggle - turn haptics on or off")
        self:Print("  /butthurt test [damage|heal|death|level_up|achievement]")
        self:Print("  /butthurt debug - print events to chat")
    end
end

function Butthurt:OnInitialize()
    ButthurtDB = ButthurtDB or {}
    for key, value in pairs(defaults) do
        if ButthurtDB[key] == nil then
            ButthurtDB[key] = value
        end
    end

    SetupPixelFrame()
    self:LoadAddEvents()
    self:RegisterChatCommand("butthurt", "SlashCommand")
    self:RegisterChatCommand("bh", "SlashCommand")
    self:RegisterSettingsPanel()

    self:Debug("Loading complete")
end
