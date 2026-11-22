local _G = getfenv(0);
local LibStub = _G.LibStub;


local name = ... or "Butthurt";
---@class Butthurt
local Butthurt = LibStub("AceAddon-3.0"):GetAddon(name);
if not Butthurt then return; end


-- In-game settings window. Vibration strength lives in the desktop app, this window only decides
-- what the addon flashes, so everything here is about events and the corner pixel.
-- Blizzard's own window, checkboxes and buttons, laid out like the desktop app: events grouped by category,
-- two per line, so the window stays short.

local WINDOW_WIDTH = 470
local PAD = 16
local COLUMN_GAP = 10
local ROW_HEIGHT = 24
local ACCENT = "|cFFFF4D8D"

local window

local function CreateLabel(parent, text, font)
    local label = parent:CreateFontString(nil, "OVERLAY", font or "GameFontHighlight")
    label:SetText(text)
    label:SetJustifyH("LEFT")
    return label
end

local function CreateCheckbox(parent, text, onClick, font)
    local check = CreateFrame("CheckButton", nil, parent, "UICheckButtonTemplate")
    check:SetSize(24, 24)
    check.label = CreateLabel(check, text, font)
    check.label:SetPoint("LEFT", check, "RIGHT", 2, 1)
    check:SetScript("OnClick", function(self) onClick(self:GetChecked()) end)
    return check
end

local function CreateSwatch(parent, size)
    local swatch = parent:CreateTexture(nil, "ARTWORK")
    swatch:SetSize(size, size)
    return swatch
end

local function SetSwatchColor(swatch, colorName)
    local color = Butthurt.Colors[colorName] or Butthurt.Colors.IDLE
    swatch:SetColorTexture(unpack(color))
end

local function CreateButton(parent, text, width, onClick, height, small)
    local button = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
    button:SetSize(width, height or 22)
    button:SetText(text)
    if small then
        button:SetNormalFontObject("GameFontHighlightSmall")
        button:SetHighlightFontObject("GameFontHighlightSmall")
    end
    button:SetScript("OnClick", onClick)
    return button
end

local function CreateSlider(parent, width, minValue, maxValue, step, onChange)
    local slider = CreateFrame("Slider", nil, parent, "BackdropTemplate")
    slider:SetOrientation("HORIZONTAL")
    slider:SetSize(width, 16)
    slider:SetBackdrop({
        bgFile = "Interface\\Buttons\\UI-SliderBar-Background",
        edgeFile = "Interface\\Buttons\\UI-SliderBar-Border",
        tile = true, tileSize = 8, edgeSize = 8,
        insets = { left = 3, right = 3, top = 6, bottom = 6 },
    })
    slider:SetThumbTexture("Interface\\Buttons\\UI-SliderBar-Button-Horizontal")
    slider:SetMinMaxValues(minValue, maxValue)
    slider:SetValueStep(step)
    slider:SetObeyStepOnDrag(true)
    slider:SetScript("OnValueChanged", function(self, value) onChange(value) end)
    return slider
end

--- A thin gold-ish line, like the dividers in Blizzard's own panels.
local function CreateDivider(parent, y)
    local line = parent:CreateTexture(nil, "ARTWORK")
    line:SetColorTexture(1, 0.82, 0, 0.18)
    line:SetHeight(1)
    line:SetPoint("TOPLEFT", parent, "TOPLEFT", PAD, y)
    line:SetPoint("TOPRIGHT", parent, "TOPRIGHT", -PAD, y)
    return line
end

-- One event: color swatch, checkbox with its name, and a small Test button on the right.
local function CreateEventRow(parent, info, width)
    local row = CreateFrame("Frame", nil, parent)
    row:SetSize(width, ROW_HEIGHT)

    row.swatch = CreateSwatch(row, 9)
    row.swatch:SetPoint("LEFT", row, "LEFT", 2, 0)
    SetSwatchColor(row.swatch, info.key)

    row.check = CreateCheckbox(row, info.label, function(checked)
        Butthurt:SetEventEnabled(info.key, checked)
        Butthurt:RefreshWindow()
    end, "GameFontHighlightSmall")
    row.check:SetPoint("LEFT", row.swatch, "RIGHT", 2, 0)

    row.test = CreateButton(row, "Test", 44, function() Butthurt:Triggered(info.key, true) end, 20, true)
    row.test:SetPoint("RIGHT", row, "RIGHT", 0, 0)

    row.key = info.key
    return row
end

local function CreateWindow()
    -- BasicFrameTemplateWithInset gives us the standard title bar and close button.
    local ok, frame = pcall(CreateFrame, "Frame", "ButthurtSettingsWindow", UIParent, "BasicFrameTemplateWithInset")
    if not ok then
        frame = CreateFrame("Frame", "ButthurtSettingsWindow", UIParent, "BackdropTemplate")
        frame:SetBackdrop({
            bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
            edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
            tile = true, tileSize = 32, edgeSize = 32,
            insets = { left = 11, right = 12, top = 12, bottom = 11 },
        })
        local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
        close:SetPoint("TOPRIGHT", -4, -4)
    end

    frame:SetWidth(WINDOW_WIDTH)
    frame:SetPoint("CENTER")
    frame:SetFrameStrata("DIALOG")
    frame:SetClampedToScreen(true)
    frame:SetMovable(true)
    frame:EnableMouse(true)
    frame:RegisterForDrag("LeftButton")
    frame:SetScript("OnDragStart", frame.StartMoving)
    frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
    frame:Hide()

    -- Escape closes it like any other Blizzard window.
    table.insert(UISpecialFrames, frame:GetName())

    local version = C_AddOns and C_AddOns.GetAddOnMetadata and C_AddOns.GetAddOnMetadata(name, "Version") or ""
    local title = CreateLabel(frame, ACCENT .. "Butthurt|r  |cFF808080" .. version .. "|r", "GameFontHighlight")
    title:SetPoint("TOP", frame, "TOP", 0, -5)

    local inner = WINDOW_WIDTH - PAD * 2
    local y = -34

    -- Master switch on the left, the live corner pixel on the right.
    frame.enabled = CreateCheckbox(frame, "Haptics enabled", function(checked)
        ButthurtDB.enabled = checked
        Butthurt:RefreshWindow()
    end)
    frame.enabled:SetPoint("TOPLEFT", PAD - 4, y)

    -- One swatch per corner box, right aligned, so you can watch overlapping events land in their own box.
    -- Fixed width, so the swatches don't jump around as the text changes. Placed by RefreshBoxLabels.
    frame.pixelText = CreateLabel(frame, "", "GameFontHighlightSmall")
    frame.pixelText:SetWidth(84)
    frame.pixelText:SetPoint("TOPRIGHT", frame, "TOPRIGHT", -PAD, y - 6)
    frame.boxSwatches = {}
    for i = 1, Butthurt.MAX_BOXES do
        frame.boxSwatches[i] = CreateSwatch(frame, 10)
    end
    frame.pixelLabel = CreateLabel(frame, "Corner boxes:", "GameFontDisableSmall")
    y = y - 32

    CreateDivider(frame, y)
    y = y - 10

    -- Events, grouped like the app, two per line.
    local eventsHeader = CreateLabel(frame, "Events", "GameFontNormal")
    eventsHeader:SetPoint("TOPLEFT", PAD, y)
    local eventsHint = CreateLabel(frame, "Test flashes the corner once, even when the event is off.", "GameFontDisableSmall")
    eventsHint:SetPoint("LEFT", eventsHeader, "RIGHT", 10, 0)
    y = y - 20

    frame.eventRows = {}
    local columnWidth = (inner - COLUMN_GAP) / 2
    for _, group in ipairs(Butthurt.EventGroups) do
        local caption = CreateLabel(frame, group, "GameFontNormalSmall")
        caption:SetPoint("TOPLEFT", PAD + 2, y - 4)
        y = y - 20

        local column = 0
        for _, info in ipairs(Butthurt.EventInfo) do
            if info.group == group then
                local row = CreateEventRow(frame, info, columnWidth)
                row:SetPoint("TOPLEFT", PAD + column * (columnWidth + COLUMN_GAP), y)
                frame.eventRows[#frame.eventRows + 1] = row

                column = column + 1
                if column == 2 then
                    column = 0
                    y = y - ROW_HEIGHT
                end
            end
        end
        if column == 1 then y = y - ROW_HEIGHT end
        y = y - 4
    end
    y = y - 6

    CreateDivider(frame, y)
    y = y - 12

    -- Flash length
    local flashHeader = CreateLabel(frame, "Flash length", "GameFontNormal")
    flashHeader:SetPoint("TOPLEFT", PAD, y)
    frame.flashValue = CreateLabel(frame, "", "GameFontHighlightSmall")
    frame.flashValue:SetPoint("LEFT", flashHeader, "RIGHT", 8, 0)
    local flashHint = CreateLabel(frame, "Raise this if the app misses quick hits.", "GameFontDisableSmall")
    flashHint:SetPoint("TOPRIGHT", frame, "TOPRIGHT", -PAD, y - 2)
    y = y - 22

    frame.flashSlider = CreateSlider(frame, inner - 8, 0.1, 1.0, 0.05, function(value)
        ButthurtDB.flashDuration = value
        frame.flashValue:SetFormattedText("%.2f s", value)
    end)
    frame.flashSlider:SetPoint("TOPLEFT", PAD + 4, y)
    y = y - 26

    -- Corner boxes: how many events can be lit at once, and how big each box is. The app works both out
    -- by itself, nothing to set there.
    local boxesHeader = CreateLabel(frame, "Corner boxes", "GameFontNormal")
    boxesHeader:SetPoint("TOPLEFT", PAD, y)
    frame.boxesValue = CreateLabel(frame, "", "GameFontHighlightSmall")
    frame.boxesValue:SetPoint("LEFT", boxesHeader, "RIGHT", 8, 0)
    local boxesHint = CreateLabel(frame, "More boxes, fewer events missed.", "GameFontDisableSmall")
    boxesHint:SetPoint("TOPRIGHT", frame, "TOPRIGHT", -PAD, y - 2)
    y = y - 20

    local half = (inner - COLUMN_GAP) / 2
    local countCaption = CreateLabel(frame, "Count", "GameFontDisableSmall")
    countCaption:SetPoint("TOPLEFT", PAD + 4, y)
    local sizeCaption = CreateLabel(frame, "Size", "GameFontDisableSmall")
    sizeCaption:SetPoint("TOPLEFT", PAD + half + COLUMN_GAP + 4, y)
    y = y - 14

    local function OnBoxSettingChanged(key, value)
        value = math.floor(value + 0.5)
        if ButthurtDB[key] ~= value then
            ButthurtDB[key] = value
            Butthurt:LayoutBoxes()
        end
        Butthurt:RefreshBoxLabels()
    end

    frame.boxCountSlider = CreateSlider(frame, half - 8, 1, Butthurt.MAX_BOXES, 1, function(value)
        OnBoxSettingChanged("boxCount", value)
    end)
    frame.boxCountSlider:SetPoint("TOPLEFT", PAD + 4, y)
    frame.boxSizeSlider = CreateSlider(frame, half - 8, Butthurt.MIN_BOX_SIZE, Butthurt.MAX_BOX_SIZE, 1, function(value)
        OnBoxSettingChanged("boxSize", value)
    end)
    frame.boxSizeSlider:SetPoint("TOPLEFT", PAD + half + COLUMN_GAP + 4, y)
    y = y - 26

    -- Misc
    frame.debug = CreateCheckbox(frame, "Print events to chat (debug)", function(checked)
        ButthurtDB.debug = checked
    end, "GameFontHighlightSmall")
    frame.debug:SetPoint("TOPLEFT", PAD - 4, y)
    y = y - 28

    local footer = CreateLabel(frame, "Vibration strength, PiShock and the rest are set in the Butthurt app.", "GameFontDisableSmall")
    footer:SetPoint("TOPLEFT", PAD, y)
    footer:SetWidth(inner)
    y = y - 14 - 12

    frame:SetHeight(-y)

    frame:SetScript("OnShow", function() Butthurt:RefreshWindow() end)

    -- Keep the pixel preview up to date while the window is open.
    local elapsedSinceUpdate = 0
    frame:SetScript("OnUpdate", function(self, elapsed)
        elapsedSinceUpdate = elapsedSinceUpdate + elapsed
        if elapsedSinceUpdate < 0.05 then return; end
        elapsedSinceUpdate = 0

        local count = Butthurt:GetBoxCount()
        local lit, newest = 0, nil
        for i, swatch in ipairs(self.boxSwatches) do
            local box = Butthurt.boxes[i]
            local shown = i <= count and box ~= nil
            swatch:SetShown(shown)
            if shown then
                SetSwatchColor(swatch, box.color)
                if box.color ~= "IDLE" then
                    lit = lit + 1
                    if not newest or box.litAt > newest.litAt then newest = box end
                end
            end
        end

        if not ButthurtDB.enabled and lit == 0 then
            self.pixelText:SetText("|cFFEF4444haptics off|r")
        elseif lit == 0 then
            self.pixelText:SetText("idle")
        elseif lit == 1 then
            self.pixelText:SetText((newest.color:lower():gsub("_", " ")))
        else
            self.pixelText:SetFormattedText("%d at once", lit)
        end
    end)

    return frame
end

function Butthurt:RefreshWindow()
    if not window then return; end

    window.enabled:SetChecked(ButthurtDB.enabled)
    window.debug:SetChecked(ButthurtDB.debug)
    window.flashSlider:SetValue(ButthurtDB.flashDuration)
    window.flashValue:SetFormattedText("%.2f s", ButthurtDB.flashDuration)
    window.boxCountSlider:SetValue(self:GetBoxCount())
    window.boxSizeSlider:SetValue(self:GetBoxSize())
    self:RefreshBoxLabels()

    for _, row in ipairs(window.eventRows) do
        local on = self:IsEventEnabled(row.key)
        row.check:SetChecked(on)
        -- Turned off events (or everything, with haptics off) read as grayed out.
        if on then row.check.label:SetTextColor(1, 1, 1) else row.check.label:SetTextColor(0.5, 0.5, 0.5) end
        row:SetAlpha(ButthurtDB.enabled and 1 or 0.5)
    end
end

--- The "4 boxes, 6 px" text, and the box swatches in a row left of the status text, as many as there are boxes.
function Butthurt:RefreshBoxLabels()
    if not window then return; end
    local count = self:GetBoxCount()
    window.boxesValue:SetFormattedText("%d %s, %d px", count, count == 1 and "box" or "boxes", self:GetBoxSize())

    for i, swatch in ipairs(window.boxSwatches) do
        swatch:ClearAllPoints()
        swatch:SetPoint("RIGHT", window.pixelText, "LEFT", -6 - (count - i) * 12, 0)
    end
    window.pixelLabel:ClearAllPoints()
    window.pixelLabel:SetPoint("RIGHT", window.boxSwatches[1], "LEFT", -6, 0)
end

function Butthurt:ToggleWindow()
    window = window or CreateWindow()
    window:SetShown(not window:IsShown())
end

-- Entry in the AddOns menu next to the minimap (see AddonCompartmentFunc in the .toc).
function Butthurt_OnAddonCompartmentClick()
    Butthurt:ToggleWindow()
end

--- Adds a page under Options > AddOns with a button that opens the window.
function Butthurt:RegisterSettingsPanel()
    if not (Settings and Settings.RegisterCanvasLayoutCategory and Settings.RegisterAddOnCategory) then return; end

    local panel = CreateFrame("Frame")
    local title = CreateLabel(panel, ACCENT .. "Butthurt|r", "GameFontNormalLarge")
    title:SetPoint("TOPLEFT", 16, -16)

    local text = CreateLabel(panel, "Get physically notified when things happen in game. Settings live in their own window, you can also open it with /butthurt.", "GameFontHighlight")
    text:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -8)
    text:SetWidth(520)

    local open = CreateButton(panel, "Open Butthurt settings", 200, function()
        if SettingsPanel and SettingsPanel:IsShown() then HideUIPanel(SettingsPanel) end
        window = window or CreateWindow()
        window:Show()
    end)
    open:SetPoint("TOPLEFT", text, "BOTTOMLEFT", 0, -16)

    local category = Settings.RegisterCanvasLayoutCategory(panel, "Butthurt")
    Settings.RegisterAddOnCategory(category)
end
