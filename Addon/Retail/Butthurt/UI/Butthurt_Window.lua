local _G = getfenv(0);
local LibStub = _G.LibStub;


local name = ... or "Butthurt";
---@class Butthurt
local Butthurt = LibStub("AceAddon-3.0"):GetAddon(name);
if not Butthurt then return; end


-- In-game settings window. Vibration strength lives in the desktop app, this window only decides
-- what the addon flashes, so everything here is about events and the corner pixel.

local WINDOW_WIDTH = 340
local ROW_HEIGHT = 26
local ACCENT = "|cFFFF4D8D"

local window

local function CreateLabel(parent, text, font)
    local label = parent:CreateFontString(nil, "OVERLAY", font or "GameFontHighlight")
    label:SetText(text)
    label:SetJustifyH("LEFT")
    return label
end

local function CreateCheckbox(parent, text, onClick)
    local check = CreateFrame("CheckButton", nil, parent, "UICheckButtonTemplate")
    check:SetSize(24, 24)
    check.label = CreateLabel(check, text)
    check.label:SetPoint("LEFT", check, "RIGHT", 4, 0)
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

local function CreateButton(parent, text, width, onClick)
    local button = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
    button:SetSize(width, 22)
    button:SetText(text)
    button:SetScript("OnClick", onClick)
    return button
end

local function CreateSlider(parent, minValue, maxValue, step, onChange)
    local slider = CreateFrame("Slider", nil, parent, "BackdropTemplate")
    slider:SetOrientation("HORIZONTAL")
    slider:SetSize(WINDOW_WIDTH - 60, 16)
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

local function CreateSection(parent, text, anchor, offset)
    local header = CreateLabel(parent, text, "GameFontNormal")
    header:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 0, offset or -16)
    return header
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

    frame:SetSize(WINDOW_WIDTH, 100)
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

    local title = CreateLabel(frame, ACCENT .. "Butthurt|r", "GameFontHighlight")
    title:SetPoint("TOP", frame, "TOP", 0, -5)

    local version = C_AddOns and C_AddOns.GetAddOnMetadata and C_AddOns.GetAddOnMetadata(name, "Version") or ""
    local subtitle = CreateLabel(frame, "Haptic feedback " .. version, "GameFontDisableSmall")
    subtitle:SetPoint("TOPLEFT", frame, "TOPLEFT", 16, -34)

    -- Master switch
    frame.enabled = CreateCheckbox(frame, "Haptics enabled", function(checked)
        ButthurtDB.enabled = checked
        Butthurt:RefreshWindow()
    end)
    frame.enabled:SetPoint("TOPLEFT", subtitle, "BOTTOMLEFT", -4, -8)

    -- Live preview of the corner pixel, handy to see the addon is doing something.
    local pixelLabel = CreateLabel(frame, "Corner pixel:", "GameFontHighlightSmall")
    pixelLabel:SetPoint("TOPLEFT", frame.enabled, "BOTTOMLEFT", 4, -6)
    frame.pixelSwatch = CreateSwatch(frame, 12)
    frame.pixelSwatch:SetPoint("LEFT", pixelLabel, "RIGHT", 6, 0)
    frame.pixelText = CreateLabel(frame, "", "GameFontHighlightSmall")
    frame.pixelText:SetPoint("LEFT", frame.pixelSwatch, "RIGHT", 6, 0)

    -- Events
    local eventsHeader = CreateSection(frame, "Events", pixelLabel, -16)
    frame.eventRows = {}
    local previous = eventsHeader
    for index, info in ipairs(Butthurt.EventInfo) do
        local row = CreateFrame("Frame", nil, frame)
        row:SetSize(WINDOW_WIDTH - 32, ROW_HEIGHT)
        row:SetPoint("TOPLEFT", previous, "BOTTOMLEFT", 0, index == 1 and -6 or 0)

        row.swatch = CreateSwatch(row, 10)
        row.swatch:SetPoint("LEFT", row, "LEFT", 2, 0)
        SetSwatchColor(row.swatch, info.key)

        row.check = CreateCheckbox(row, info.label, function(checked)
            Butthurt:SetEventEnabled(info.key, checked)
        end)
        row.check:SetPoint("LEFT", row.swatch, "RIGHT", 4, 0)

        row.test = CreateButton(row, "Test", 56, function()
            Butthurt:Triggered(info.key, true)
        end)
        row.test:SetPoint("RIGHT", row, "RIGHT", 0, 0)

        row.key = info.key
        frame.eventRows[#frame.eventRows + 1] = row
        previous = row
    end

    -- Flash length
    local flashHeader = CreateSection(frame, "Flash length", previous, -14)
    frame.flashValue = CreateLabel(frame, "", "GameFontHighlightSmall")
    frame.flashValue:SetPoint("LEFT", flashHeader, "RIGHT", 8, 0)
    frame.flashSlider = CreateSlider(frame, 0.1, 1.0, 0.05, function(value)
        ButthurtDB.flashDuration = value
        frame.flashValue:SetFormattedText("%.2f s", value)
    end)
    frame.flashSlider:SetPoint("TOPLEFT", flashHeader, "BOTTOMLEFT", 4, -10)
    local flashHint = CreateLabel(frame, "Raise this if the app misses quick hits.", "GameFontDisableSmall")
    flashHint:SetPoint("TOPLEFT", frame.flashSlider, "BOTTOMLEFT", -4, -6)

    -- Misc
    frame.debug = CreateCheckbox(frame, "Print events to chat (debug)", function(checked)
        ButthurtDB.debug = checked
    end)
    frame.debug:SetPoint("TOPLEFT", flashHint, "BOTTOMLEFT", -4, -10)

    local footer = CreateLabel(frame, "Vibration strength and length are set in the Butthurt app.", "GameFontDisableSmall")
    footer:SetPoint("TOPLEFT", frame.debug, "BOTTOMLEFT", 4, -10)
    footer:SetWidth(WINDOW_WIDTH - 32)

    -- Grow the window to fit everything above. The layout isn't known until the frame has been drawn once.
    frame:SetHeight(440)
    local function FitHeight()
        local top, bottom = frame:GetTop(), footer:GetBottom()
        if top and bottom then
            frame:SetHeight(top - bottom + 18)
        end
    end

    frame:SetScript("OnShow", function()
        Butthurt:RefreshWindow()
        FitHeight()
        C_Timer.After(0, FitHeight)
    end)

    -- Keep the pixel preview up to date while the window is open.
    local elapsedSinceUpdate = 0
    frame:SetScript("OnUpdate", function(self, elapsed)
        elapsedSinceUpdate = elapsedSinceUpdate + elapsed
        if elapsedSinceUpdate < 0.05 then return; end
        elapsedSinceUpdate = 0

        SetSwatchColor(self.pixelSwatch, Butthurt.currentColor)
        if Butthurt.currentColor == "IDLE" then
            self.pixelText:SetText(ButthurtDB.enabled and "idle" or "|cFFEF4444haptics off|r")
        else
            self.pixelText:SetText((Butthurt.currentColor:lower():gsub("_", " ")))
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

    for _, row in ipairs(window.eventRows) do
        row.check:SetChecked(self:IsEventEnabled(row.key))
        row.check:SetAlpha(ButthurtDB.enabled and 1 or 0.5)
    end
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
