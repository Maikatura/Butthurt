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
-- Keep these in sync with WowEvents.cs in the desktop app. Every channel is 0, 0.5 or 1, and no two
-- colors are closer than half a channel, so the app can always tell them apart.
Butthurt.Colors = {
    IDLE          = { 0, 0, 0, 1 },
    DAMAGE        = { 1, 0, 0, 1 },
    HEAL          = { 0, 1, 0, 1 },
    LEVEL_UP      = { 0, 0, 1, 1 },
    DEATH         = { 1, 0, 1, 1 },
    ACHIEVEMENT   = { 1, 1, 0, 1 },
    ENTER_COMBAT  = { 1, 0.5, 0, 1 },
    LEAVE_COMBAT  = { 0, 0.5, 1, 1 },
    CROWD_CONTROL = { 0.5, 0, 1, 1 },
    REVIVE        = { 1, 1, 1, 1 },
    BOSS_PULL     = { 1, 0, 0.5, 1 },
    BOSS_KILL     = { 0, 1, 1, 1 },
    QUEST_DONE    = { 0.5, 1, 0, 1 },
    PVP_KILL      = { 0.5, 0.5, 0.5, 1 },
    WHISPER       = { 0, 1, 0.5, 1 },
    READY_CHECK   = { 1, 0.5, 1, 1 },
};

-- Shown in the settings window, grouped like the desktop app.
Butthurt.EventGroups = { "Combat", "Progress", "Group & PvP", "Social" };

Butthurt.EventInfo = {
    { key = "DAMAGE",        label = "Taking damage",    group = "Combat" },
    { key = "HEAL",          label = "Getting healed",   group = "Combat" },
    { key = "CROWD_CONTROL", label = "Crowd controlled", group = "Combat" },
    { key = "ENTER_COMBAT",  label = "Entering combat",  group = "Combat" },
    { key = "LEAVE_COMBAT",  label = "Leaving combat",   group = "Combat" },
    { key = "DEATH",         label = "Dying",            group = "Combat" },
    { key = "REVIVE",        label = "Back to life",     group = "Combat" },
    { key = "LEVEL_UP",      label = "Level up",         group = "Progress" },
    { key = "ACHIEVEMENT",   label = "Achievement",      group = "Progress" },
    { key = "QUEST_DONE",    label = "Quest turned in",  group = "Progress" },
    { key = "BOSS_PULL",     label = "Boss pulled",      group = "Group & PvP" },
    { key = "BOSS_KILL",     label = "Boss killed",      group = "Group & PvP" },
    { key = "PVP_KILL",      label = "Honorable kill",   group = "Group & PvP" },
    { key = "READY_CHECK",   label = "Ready check",      group = "Group & PvP" },
    { key = "WHISPER",       label = "Whisper",          group = "Social" },
};

-- The corner is a strip of boxes: [box 1] ... [box n] [marker] [black]. Every event that's lit gets its own
-- box, so events that overlap (damage while a boss is pulled) all reach the app. The app looks for the
-- marker to work out how many boxes there are and how big they are, so both can change without telling it.
-- Keep the marker in sync with WowEvents.MarkerR/G/B in the desktop app.
Butthurt.MarkerColor = { 0.5, 0, 0.5, 1 };
Butthurt.MAX_BOXES = 8;
Butthurt.MIN_BOX_SIZE = 4;
Butthurt.MAX_BOX_SIZE = 12;

local defaults = {
    enabled = true,
    debug = false,
    flashDuration = 0.2,
    boxCount = 4,
    boxSize = 6, -- in screen pixels
    events = {},
};

-- Parented to nothing so the boxes survive Alt+Z (hidden UI) and ignore the UI scale.
Butthurt.pixelFlashFrame = CreateFrame("Frame", "ButthurtPixelFlashFrame", nil)

-- One entry per box: its texture, the event it shows ("IDLE" when off) and when it lit up.
Butthurt.boxes = {}

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

--- True when an event is about the player. Events that don't pass a unit (or pass something else, like an
--- index) count as the player. A unit the game hides as a secret value can't be compared, so it counts as not.
function Butthurt:IsPlayerUnit(unit)
    if type(unit) ~= "string" then return true; end
    local ok, isPlayer = pcall(function() return unit == "player" end)
    return ok and isPlayer
end

function Butthurt:IsEventEnabled(colorName)
    return ButthurtDB.events[colorName] ~= false
end

function Butthurt:SetEventEnabled(colorName, enabled)
    ButthurtDB.events[colorName] = enabled
end

function Butthurt:GetBoxCount()
    return math.max(1, math.min(Butthurt.MAX_BOXES, math.floor(ButthurtDB.boxCount or defaults.boxCount)))
end

function Butthurt:GetBoxSize()
    return math.max(Butthurt.MIN_BOX_SIZE, math.min(Butthurt.MAX_BOX_SIZE, math.floor(ButthurtDB.boxSize or defaults.boxSize)))
end

--- The box an event goes into: the one already showing it (so ongoing damage stays in one place), else the
--- first free one, else the one that has been lit the longest.
local function PickBox(colorName)
    local free, oldest
    for i = 1, Butthurt:GetBoxCount() do
        local box = Butthurt.boxes[i]
        if box.color == colorName then return box; end
        if box.color == "IDLE" then
            free = free or box
        elseif not oldest or box.litAt < oldest.litAt then
            oldest = box
        end
    end
    return free or oldest
end

local function SetBoxIdle(box)
    box.flashId = (box.flashId or 0) + 1
    box.color = "IDLE"
    box.texture:SetColorTexture(unpack(Butthurt.Colors.IDLE))
end

--- Flash a corner box with the color of the given event (defaults to DAMAGE).
--- force skips the on/off checks, used by the test buttons.
function Butthurt:Triggered(colorName, force)
    colorName = colorName or "DAMAGE"
    if not force and (not ButthurtDB.enabled or not self:IsEventEnabled(colorName)) then return; end
    if not Butthurt.Colors[colorName] or colorName == "IDLE" then colorName = "DAMAGE"; end

    local box = PickBox(colorName)
    if not box then return; end

    box.color = colorName
    box.litAt = GetTime()
    box.texture:SetColorTexture(unpack(Butthurt.Colors[colorName]))

    -- Only the most recent flash is allowed to turn the box off, so rapid hits keep it lit.
    box.flashId = (box.flashId or 0) + 1
    local flashId = box.flashId
    C_Timer.After(ButthurtDB.flashDuration or defaults.flashDuration, function()
        if box.flashId == flashId then SetBoxIdle(box); end
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

--- Screen pixels to UI units for a frame with an effective scale of 1 (ours ignores the UI scale).
local function PixelsToUnits(pixels)
    if PixelUtil and PixelUtil.GetPixelToUIUnitFactor then
        return pixels * PixelUtil.GetPixelToUIUnitFactor()
    end
    local _, height = GetPhysicalScreenSize()
    return pixels * 768 / height
end

local function CreateBoxTexture(frame)
    local texture = frame:CreateTexture(nil, "OVERLAY")
    texture:SetColorTexture(unpack(Butthurt.Colors.IDLE))
    return texture
end

--- Places the boxes, the marker and the black end box. Called on load, when the box settings change and
--- when the game window changes size (box sizes are in screen pixels).
function Butthurt:LayoutBoxes()
    local frame = Butthurt.pixelFlashFrame
    local count = self:GetBoxCount()
    local size = PixelsToUnits(self:GetBoxSize())

    for i = 1, Butthurt.MAX_BOXES do
        local box = Butthurt.boxes[i]
        if not box then
            box = { texture = CreateBoxTexture(frame), color = "IDLE", litAt = 0 }
            Butthurt.boxes[i] = box
        end
        SetBoxIdle(box)
        box.texture:ClearAllPoints()
        box.texture:SetSize(size, size)
        box.texture:SetPoint("TOPLEFT", frame, "TOPLEFT", (i - 1) * size, 0)
        box.texture:SetShown(i <= count)
    end

    frame.marker = frame.marker or CreateBoxTexture(frame)
    frame.marker:SetColorTexture(unpack(Butthurt.MarkerColor))
    frame.marker:ClearAllPoints()
    frame.marker:SetSize(size, size)
    frame.marker:SetPoint("TOPLEFT", frame, "TOPLEFT", count * size, 0)

    frame.endBox = frame.endBox or CreateBoxTexture(frame)
    frame.endBox:ClearAllPoints()
    frame.endBox:SetSize(size, size)
    frame.endBox:SetPoint("TOPLEFT", frame, "TOPLEFT", (count + 1) * size, 0)

    frame:SetSize((count + 2) * size, size)
end

local function SetupPixelFrame()
    local frame = Butthurt.pixelFlashFrame
    frame:SetFrameStrata("TOOLTIP")
    frame:SetFrameLevel(10000)
    frame:SetPoint("TOPLEFT", UIParent, "TOPLEFT")
    frame:SetIgnoreParentScale(true)
    frame:SetScale(1)
    Butthurt:LayoutBoxes()
    frame:Show()

    -- Box sizes are in screen pixels, so lay them out again when the game window changes size.
    frame:RegisterEvent("DISPLAY_SIZE_CHANGED")
    frame:RegisterEvent("UI_SCALE_CHANGED")
    frame:SetScript("OnEvent", function() Butthurt:LayoutBoxes() end)
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
            self:Print("Unknown event. Try: damage, heal, death, level_up, achievement, enter_combat, boss_kill, whisper…")
            return
        end
        self:Print("Testing " .. colorName)
        self:Triggered(colorName, true)
    elseif command == "debug" then
        ButthurtDB.debug = not ButthurtDB.debug
        self:Print("Debug " .. (ButthurtDB.debug and "on" or "off"))
    elseif command == "boxes" then
        local count = tonumber(arg)
        if count then
            ButthurtDB.boxCount = math.max(1, math.min(Butthurt.MAX_BOXES, math.floor(count)))
            self:LayoutBoxes()
            self:RefreshWindow()
        end
        self:Print(("%d boxes of %d px. Use /butthurt boxes 1-%d to change it."):format(self:GetBoxCount(), self:GetBoxSize(), Butthurt.MAX_BOXES))
    else
        self:Print("Commands:")
        self:Print("  /butthurt - open the settings window")
        self:Print("  /butthurt toggle - turn haptics on or off")
        self:Print("  /butthurt test [damage|heal|death|level_up|achievement|low_health|enter_combat|…]")
        self:Print("  /butthurt boxes [1-" .. Butthurt.MAX_BOXES .. "] - how many corner boxes events can use")
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
