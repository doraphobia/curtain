/* AE2Unity exporter
 * After Effects 2026 ExtendScript panel.
 *
 * Install options:
 * - Run with File > Scripts > Run Script File...
 * - Or copy into the After Effects ScriptUI Panels folder to dock it.
 */

(function ae2UnityExporter(thisObj) {
    var SCRIPT_NAME = "AE2Unity";
    var SCHEMA_VERSION = "0.1.0";
    var SETTINGS_SECTION = "AE2Unity";
    var LEGACY_SETTINGS_SECTIONS = ["ae2unityshader", "DuoCurtainAE2UnityShader"];
    var DEFAULT_UNITY_EXPORT_RELATIVE_PATH = "Assets/AE2Unity/Exports";
    var DEFAULT_MEDIA_EXPORT_RELATIVE_PATH = "Assets/AE2Unity/Media";
    var BRIDGE_FOLDER_NAME = ".ae2unitybridge";
    var UI_LABEL_WIDTH = 136;
    var UI_BUTTON_WIDTH = 104;
    var UI_SMALL_WIDTH = 96;
    var UI_MEDIUM_WIDTH = 190;
    var UI_FIELD_WIDTH = 480;
    var UI_PANEL_MIN_WIDTH = 300;
    var UI_PANEL_MIN_HEIGHT = 360;
    var UI_COMPACT_BREAKPOINT = 620;
    var UI_COMPACT_HORIZONTAL_MARGIN = 36;
    var UI_COMPACT_ROW_HEIGHT = 52;
    var UI_COMPACT_TALL_ROW_HEIGHT = 82;
    var UI_COMPACT_OPTIONS_ROW_HEIGHT = 34;
    var UI_COMPACT_MEDIA_OPTIONS_HEIGHT = 112;
    var UI_COMPACT_RESULT_GROUP_HEIGHT = 178;
    var UI_COMPACT_RESULT_STATUS_HEIGHT = 132;
    var UI_FULL_WINDOW_SIZE = [860, 560];
    var UI_COMPACT_WINDOW_SIZE = [340, 560];
    var UI_MAX_VISIBLE_HEIGHT = 10000;
    var UI_NORMAL_PANEL_MARGINS = [16, 14, 16, 14];
    var UI_COMPACT_PANEL_MARGINS = [12, 10, 10, 10];
    var standaloneWindows = [];

    function buildPanel(owner, options) {
        options = options || {};
        var isDockedPanel = isPanelOwner(owner);
        var panel = isDockedPanel
            ? owner
            : new Window("palette", options.title || SCRIPT_NAME, undefined, { resizeable: true });

        panel.orientation = "column";
        panel.alignChildren = ["fill", "top"];
        panel.spacing = 10;
        panel.margins = UI_NORMAL_PANEL_MARGINS;
        panel.minimumSize = [UI_PANEL_MIN_WIDTH, UI_PANEL_MIN_HEIGHT];
        panel.preferredSize = options.forceCompact ? UI_COMPACT_WINDOW_SIZE : UI_FULL_WINDOW_SIZE;

        panel.ae2unityCompactActive = false;
        panel.ae2unityForceCompact = options.forceCompact === true;
        panel.ae2unityIsStandaloneWindow = !isDockedPanel;

        var title = panel.add("statictext", undefined, "Export compositions directly into a Unity project");
        title.alignment = ["fill", "top"];
        title.preferredSize.height = 26;
        compactHide(title);
        setHelpTip(title, "Export AE composition data, media, or both into a selected Unity project.");

        var standaloneModeGroup = panel.add("group");
        standaloneModeGroup.orientation = "row";
        standaloneModeGroup.alignChildren = ["left", "center"];
        standaloneModeGroup.alignment = ["fill", "top"];
        standaloneModeGroup.spacing = 8;
        standaloneModeGroup.margins = 0;
        standaloneModeGroup.ae2unityStandaloneOnly = true;
        setHelpTip(standaloneModeGroup, "Standalone window size shortcuts.");
        var compactWindowButton = standaloneModeGroup.add("button", undefined, "Compact");
        fixedControl(compactWindowButton, 112, 26);
        compactWindowButton.ae2unityPreserveCompactWidth = true;
        setHelpTip(compactWindowButton, "Switch this standalone window to compact mode (Ctrl/Cmd+Shift+C).");
        var fullWindowButton = standaloneModeGroup.add("button", undefined, "Full Size");
        fixedControl(fullWindowButton, 112, 26);
        fullWindowButton.ae2unityPreserveCompactWidth = true;
        setHelpTip(fullWindowButton, "Restore this standalone window to the full layout (Ctrl/Cmd+Shift+F).");
        panel.ae2unityStandaloneControls = {
            group: standaloneModeGroup,
            compactButton: compactWindowButton,
            fullButton: fullWindowButton
        };

        var compactPagerGroup = panel.add("group");
        compactPagerGroup.orientation = "row";
        compactPagerGroup.alignChildren = ["left", "center"];
        compactPagerGroup.alignment = ["fill", "top"];
        compactPagerGroup.spacing = 4;
        compactPagerGroup.margins = 0;
        var compactPrevButton = compactPagerGroup.add("button", undefined, "Up");
        fixedControl(compactPrevButton, 38, 24);
        compactPrevButton.ae2unityPreserveCompactWidth = true;
        setHelpTip(compactPrevButton, "Show the previous compact panel section.");
        var compactPageText = compactPagerGroup.add("statictext", undefined, "Compact");
        compactPageText.alignment = ["fill", "center"];
        compactPageText.minimumSize.width = 56;
        setHelpTip(compactPageText, "Current compact panel section.");
        var compactPageScrollbar = compactPagerGroup.add("scrollbar", undefined, 0, 0, 0);
        fixedControl(compactPageScrollbar, 32, 16);
        compactPageScrollbar.ae2unityPreserveCompactWidth = true;
        setHelpTip(compactPageScrollbar, "Drag to switch compact panel sections.");
        var compactRunExportButton = compactPagerGroup.add("button", undefined, "Run");
        fixedControl(compactRunExportButton, 42, 24);
        compactRunExportButton.ae2unityPreserveCompactWidth = true;
        setHelpTip(compactRunExportButton, "Run the selected export workflow from compact mode.");
        var compactNextButton = compactPagerGroup.add("button", undefined, "Down");
        fixedControl(compactNextButton, 46, 24);
        compactNextButton.ae2unityPreserveCompactWidth = true;
        setHelpTip(compactNextButton, "Show the next compact panel section.");
        var compactFullWindowButton = compactPagerGroup.add("button", undefined, "Full");
        fixedControl(compactFullWindowButton, 42, 24);
        compactFullWindowButton.ae2unityPreserveCompactWidth = true;
        setHelpTip(compactFullWindowButton, "Open a full standalone AE2Unity window.");

        panel.ae2unityCompactPager = {
            group: compactPagerGroup,
            prevButton: compactPrevButton,
            nextButton: compactNextButton,
            pageText: compactPageText,
            scrollbar: compactPageScrollbar,
            runButton: compactRunExportButton,
            fullButton: compactFullWindowButton,
            pageIndex: 0,
            pages: []
        };

        var projectGroup = createFormRow(panel, "Unity Project", "Select the Unity project that will receive exported assets.");
        projectGroup.ae2unityCompactVisibleHeight = UI_COMPACT_ROW_HEIGHT;
        var projectDropdown = projectGroup.add("dropdownlist", undefined, []);
        stretchControl(projectDropdown, 320, 220);
        setHelpTip(projectDropdown, "Choose a Unity Hub project to export into.");
        var refreshProjectsButton = projectGroup.add("button", undefined, "Refresh");
        fixedControl(refreshProjectsButton, UI_BUTTON_WIDTH);
        compactHide(refreshProjectsButton);
        setHelpTip(refreshProjectsButton, "Reload the project list from Unity Hub.");
        var chooseProjectButton = projectGroup.add("button", undefined, "Choose");
        fixedControl(chooseProjectButton, UI_BUTTON_WIDTH);
        compactHide(chooseProjectButton);
        setHelpTip(chooseProjectButton, "Manually choose a Unity project folder.");

        var pathGroup = createFormRow(panel, "Path", "Absolute folder path for the selected Unity project.");
        compactHide(pathGroup);
        pathGroup.ae2unityCompactVisibleHeight = UI_COMPACT_ROW_HEIGHT;
        var projectPathText = pathGroup.add("edittext", undefined, loadSetting("UnityProjectPath", ""));
        stretchControl(projectPathText, UI_FIELD_WIDTH, 260);
        setHelpTip(projectPathText, "Absolute path to the Unity project root.");

        var exportPathGroup = createFormRow(panel, ".ae2shader Folder", "Unity Assets-relative folder for .ae2shader exports.");
        compactHide(exportPathGroup);
        exportPathGroup.ae2unityCompactVisibleHeight = UI_COMPACT_ROW_HEIGHT;
        var relativePathText = exportPathGroup.add("edittext", undefined, loadSetting("UnityExportRelativePath", DEFAULT_UNITY_EXPORT_RELATIVE_PATH));
        stretchControl(relativePathText, UI_FIELD_WIDTH, 260);
        setHelpTip(relativePathText, "Assets-relative folder where Unity imports .ae2shader files.");

        var compGroup = createFormRow(panel, "Composition", "Choose the AE composition source for this export.");
        compGroup.ae2unityCompositionGroup = true;
        compGroup.ae2unityCompactVisibleHeight = UI_COMPACT_TALL_ROW_HEIGHT;
        var compSourceDropdown = compGroup.add("dropdownlist", undefined, ["Current Active Comp", "Specific Comp"]);
        fixedControl(compSourceDropdown, UI_MEDIUM_WIDTH);
        setHelpTip(compSourceDropdown, "Use the active comp or choose a specific comp from the project.");
        compSourceDropdown.selection = loadSetting("CompositionSource", "active") === "specific" ? 1 : 0;
        panel.ae2unityCompSourceDropdown = compSourceDropdown;
        var compDropdown = compGroup.add("dropdownlist", undefined, []);
        compDropdown.ae2unitySpecificCompOnly = true;
        stretchControl(compDropdown, 300, 220);
        setHelpTip(compDropdown, "Select the specific composition to export.");
        var refreshCompsButton = compGroup.add("button", undefined, "Refresh");
        fixedControl(refreshCompsButton, UI_BUTTON_WIDTH);
        compactHide(refreshCompsButton);
        setHelpTip(refreshCompsButton, "Reload compositions from the current AE project.");

        var modeGroup = createFormRow(panel, "Export Mode", "Choose which AE-to-Unity workflow to run.");
        modeGroup.ae2unityCompactVisibleHeight = UI_COMPACT_ROW_HEIGHT;
        var exportModeDropdown = modeGroup.add("dropdownlist", undefined, [
            "Bridge: Unity prefab",
            "Bridge + video",
            "Video only",
            "Direct to Assets",
            "Manual folder"
        ]);
        stretchControl(exportModeDropdown, UI_FIELD_WIDTH, 320);
        setHelpTip(exportModeDropdown, "Bridge creates the Unity prefab/assets. Video modes use Adobe Media Encoder. Direct/manual save .ae2shader files without the bridge receiver.");
        exportModeDropdown.selection = getExportModeSelection(loadSetting("ExportMode", "bridge"));

        var mediaFolderGroup = createFormRow(panel, "Media Folder", "Unity Assets-relative folder for rendered media.");
        compactHide(mediaFolderGroup);
        mediaFolderGroup.ae2unityCompactVisibleHeight = UI_COMPACT_ROW_HEIGHT;
        var mediaPathText = mediaFolderGroup.add("edittext", undefined, loadSetting("MediaExportRelativePath", DEFAULT_MEDIA_EXPORT_RELATIVE_PATH));
        stretchControl(mediaPathText, UI_FIELD_WIDTH, 260);
        setHelpTip(mediaPathText, "Assets-relative folder where AME-rendered media will be saved.");

        var mediaOptionsGroup = createFormRow(panel, "Media", "Media Encoder output format and template controls.");
        compactHide(mediaOptionsGroup);
        mediaOptionsGroup.ae2unityCompactVisibleHeight = UI_COMPACT_MEDIA_OPTIONS_HEIGHT;
        var mediaExtensionDropdown = mediaOptionsGroup.add("dropdownlist", undefined, ["mp4", "mov", "webm"]);
        fixedControl(mediaExtensionDropdown, UI_SMALL_WIDTH);
        setHelpTip(mediaExtensionDropdown, "Choose the output file extension for the rendered media.");
        mediaExtensionDropdown.selection = getDropdownSelectionByText(mediaExtensionDropdown, loadSetting("MediaExtension", "mp4"));
        var outputTemplateDropdown = mediaOptionsGroup.add("dropdownlist", undefined, []);
        stretchControl(outputTemplateDropdown, 260, 190);
        setHelpTip(outputTemplateDropdown, "Choose the AE Output Module template used before queueing in AME.");
        var refreshTemplatesButton = mediaOptionsGroup.add("button", undefined, "Templates");
        fixedControl(refreshTemplatesButton, UI_BUTTON_WIDTH);
        setHelpTip(refreshTemplatesButton, "Reload Output Module templates for the selected comp.");
        var startAmeCheckbox = mediaOptionsGroup.add("checkbox", undefined, "Start AME");
        fixedControl(startAmeCheckbox, 110);
        setHelpTip(startAmeCheckbox, "Start Adobe Media Encoder after adding the render queue item.");
        startAmeCheckbox.value = loadSetting("StartMediaEncoder", "true") !== "false";

        var optionsGroup = createFormRow(panel, "", "Optional export behaviors.");
        compactHide(optionsGroup);
        optionsGroup.ae2unityCompactVisibleHeight = UI_COMPACT_OPTIONS_ROW_HEIGHT;
        var referenceFramesCheckbox = optionsGroup.add("checkbox", undefined, "Reference frames");
        fixedControl(referenceFramesCheckbox, 170);
        setHelpTip(referenceFramesCheckbox, "Export PNG reference frames for visual comparison in Unity.");
        referenceFramesCheckbox.value = loadSetting("ExportReferenceFrames", "true") !== "false";
        var generateShaderCheckbox = optionsGroup.add("checkbox", undefined, "Generate");
        fixedControl(generateShaderCheckbox, 120);
        setHelpTip(generateShaderCheckbox, "Ask Unity to generate shader and material after import.");
        generateShaderCheckbox.value = loadSetting("GenerateShaderAndMaterial", "true") !== "false";
        var bakeAnimationCheckbox = optionsGroup.add("checkbox", undefined, "Bake animation");
        fixedControl(bakeAnimationCheckbox, 150);
        setHelpTip(bakeAnimationCheckbox, "Render every composition frame to PNG so complex AE comps play correctly in the generated Unity material.");
        bakeAnimationCheckbox.value = loadSetting("BakeAnimationFrames", "true") !== "false";

        var runExportButton = panel.add("button", undefined, "Run Export");
        runExportButton.alignment = ["fill", "top"];
        runExportButton.preferredSize.height = 34;
        runExportButton.ae2unityCompactVisibleHeight = 34;
        setHelpTip(runExportButton, "Run the selected export workflow.");
        var resultGroup = panel.add("group");
        resultGroup.orientation = "column";
        resultGroup.alignChildren = ["fill", "top"];
        resultGroup.alignment = ["fill", "top"];
        resultGroup.spacing = 6;
        resultGroup.margins = 0;
        resultGroup.ae2unityResultGroup = true;
        resultGroup.ae2unityCompactVisibleHeight = UI_COMPACT_RESULT_GROUP_HEIGHT;
        setHelpTip(resultGroup, "Latest Unity bridge status and export feedback.");

        var checkResultButton = resultGroup.add("button", undefined, "Check Last Bridge Result");
        checkResultButton.alignment = ["fill", "top"];
        checkResultButton.preferredSize.height = 34;
        checkResultButton.ae2unityCompactVisibleHeight = 34;
        setHelpTip(checkResultButton, "Read the latest Unity AEBridge result file.");
        var status = resultGroup.add("edittext", undefined, "Ready", { multiline: true, readonly: true, scrolling: true });
        status.alignment = ["fill", "top"];
        status.minimumSize.height = 72;
        status.preferredSize.height = 88;
        status.ae2unityResultStatus = true;
        status.ae2unityCompactVisibleHeight = UI_COMPACT_RESULT_STATUS_HEIGHT;
        stretchControl(status, UI_FIELD_WIDTH, 160);
        setHelpTip(status, "Shows export progress, warnings, and bridge results.");

        setCompactPages(panel, [
            {
                title: "Export",
                controls: [projectGroup, compGroup, modeGroup, runExportButton]
            },
            {
                title: "Result",
                controls: [resultGroup]
            },
            {
                title: "Paths",
                controls: [pathGroup, exportPathGroup, optionsGroup]
            },
            {
                title: "Media",
                controls: [mediaFolderGroup, mediaOptionsGroup]
            }
        ]);

        refreshProjectDropdown(projectDropdown, projectPathText, status);
        refreshCompositionDropdown(compDropdown, status);
        refreshOutputTemplateDropdown(outputTemplateDropdown, compSourceDropdown, compDropdown, status);
        updateMediaControls(exportModeDropdown, mediaFolderGroup, mediaOptionsGroup);

        projectDropdown.onChange = function () {
            if (!projectDropdown.selection || !projectDropdown.selection.projectPath) {
                return;
            }

            projectPathText.text = projectDropdown.selection.projectPath;
            saveSetting("UnityProjectPath", projectPathText.text);
            status.text = "Unity project set: " + projectDropdown.selection.text;
        };

        refreshProjectsButton.onClick = function () {
            refreshProjectDropdown(projectDropdown, projectPathText, status);
        };

        chooseProjectButton.onClick = function () {
            var folder = Folder.selectDialog("Choose the Unity project folder that contains Assets and ProjectSettings");
            if (!folder) {
                return;
            }

            if (!isUnityProjectFolder(folder)) {
                alert("This does not look like a Unity project. Choose the folder that contains Assets and ProjectSettings.");
                return;
            }

            projectPathText.text = folder.fsName;
            saveSetting("UnityProjectPath", folder.fsName);
            refreshProjectDropdown(projectDropdown, projectPathText, status);
            status.text = "Unity project set: " + folder.fsName;
        };

        projectPathText.onChange = function () {
            saveSetting("UnityProjectPath", projectPathText.text);
        };

        relativePathText.onChange = function () {
            saveSetting("UnityExportRelativePath", normalizeRelativePath(relativePathText.text));
        };

        compSourceDropdown.onChange = function () {
            saveSetting("CompositionSource", compSourceDropdown.selection && compSourceDropdown.selection.index === 1 ? "specific" : "active");
            refreshOutputTemplateDropdown(outputTemplateDropdown, compSourceDropdown, compDropdown, status);
            relayoutPanel(panel);
        };

        compDropdown.onChange = function () {
            if (compDropdown.selection && compDropdown.selection.projectItemIndex) {
                saveSetting("SpecificCompIndex", String(compDropdown.selection.projectItemIndex));
            }

            refreshOutputTemplateDropdown(outputTemplateDropdown, compSourceDropdown, compDropdown, status);
        };

        refreshCompsButton.onClick = function () {
            refreshCompositionDropdown(compDropdown, status);
            refreshOutputTemplateDropdown(outputTemplateDropdown, compSourceDropdown, compDropdown, status);
        };

        exportModeDropdown.onChange = function () {
            saveSetting("ExportMode", getExportModeValue(exportModeDropdown));
            updateMediaControls(exportModeDropdown, mediaFolderGroup, mediaOptionsGroup);
            relayoutPanel(panel);
        };

        mediaPathText.onChange = function () {
            saveSetting("MediaExportRelativePath", normalizeRelativePath(mediaPathText.text || DEFAULT_MEDIA_EXPORT_RELATIVE_PATH));
        };

        mediaExtensionDropdown.onChange = function () {
            if (mediaExtensionDropdown.selection) {
                saveSetting("MediaExtension", mediaExtensionDropdown.selection.text);
            }
        };

        outputTemplateDropdown.onChange = function () {
            if (outputTemplateDropdown.selection) {
                saveSetting("MediaOutputModuleTemplate", outputTemplateDropdown.selection.text);
            }
        };

        refreshTemplatesButton.onClick = function () {
            refreshOutputTemplateDropdown(outputTemplateDropdown, compSourceDropdown, compDropdown, status);
        };

        startAmeCheckbox.onClick = function () {
            saveSetting("StartMediaEncoder", startAmeCheckbox.value ? "true" : "false");
        };

        referenceFramesCheckbox.onClick = function () {
            saveSetting("ExportReferenceFrames", referenceFramesCheckbox.value ? "true" : "false");
        };

        generateShaderCheckbox.onClick = function () {
            saveSetting("GenerateShaderAndMaterial", generateShaderCheckbox.value ? "true" : "false");
        };

        bakeAnimationCheckbox.onClick = function () {
            saveSetting("BakeAnimationFrames", bakeAnimationCheckbox.value ? "true" : "false");
        };

        function runExportAction() {
            try {
                showStatusResult(panel, status, "Running export...");
                var exportResult = runConfiguredExport({
                    comp: getSelectedComposition(compSourceDropdown, compDropdown),
                    exportMode: getExportModeValue(exportModeDropdown),
                    projectPath: projectPathText.text,
                    unityExportPath: relativePathText.text,
                    mediaExportPath: mediaPathText.text,
                    mediaExtension: mediaExtensionDropdown.selection ? mediaExtensionDropdown.selection.text : "mp4",
                    outputModuleTemplate: outputTemplateDropdown.selection ? outputTemplateDropdown.selection.text : "",
                    startMediaEncoder: startAmeCheckbox.value,
                    exportReferenceFrames: referenceFramesCheckbox.value,
                    exportBakedFrames: bakeAnimationCheckbox.value,
                    generateShaderAndMaterial: generateShaderCheckbox.value
                });
                showStatusResult(panel, status, exportResult);
            } catch (error) {
                showStatusResult(panel, status, "Export failed: " + error.toString());
                alert(status.text);
            }
        }

        runExportButton.onClick = runExportAction;
        compactRunExportButton.onClick = runExportAction;

        checkResultButton.onClick = function () {
            try {
                showStatusResult(panel, status, readLastBridgeResult(projectPathText.text));
            } catch (error) {
                showStatusResult(panel, status, "Result check failed: " + error.toString());
                alert(status.text);
            }
        };

        panel.onResizing = panel.onResize = function () {
            relayoutPanel(this);
        };
        panel.onShow = function () {
            relayoutPanel(panel);
        };

        compactPrevButton.onClick = function () {
            advanceCompactPage(panel, -1);
        };
        compactNextButton.onClick = function () {
            advanceCompactPage(panel, 1);
        };
        compactPageScrollbar.onChanging = compactPageScrollbar.onChange = function () {
            setCompactPage(panel, Math.round(compactPageScrollbar.value));
        };
        compactFullWindowButton.onClick = function () {
            openStandaloneWindow(false);
        };
        compactWindowButton.onClick = function () {
            setStandaloneWindowMode(panel, true);
        };
        fullWindowButton.onClick = function () {
            setStandaloneWindowMode(panel, false);
        };

        bindCompactMouseWheelTree(panel, panel);
        bindStandaloneShortcutTree(panel, panel);
        relayoutPanel(panel);
        return panel;
    }

    function createFormRow(parent, labelText, helpTip) {
        var row = parent.add("group");
        row.orientation = "row";
        row.alignChildren = ["left", "center"];
        row.alignment = ["fill", "top"];
        row.spacing = 10;
        row.margins = 0;
        row.ae2unityIsFormRow = true;
        setHelpTip(row, helpTip);

        var label = row.add("statictext", undefined, labelText);
        label.alignment = ["left", "center"];
        label.preferredSize.width = UI_LABEL_WIDTH;
        label.minimumSize.width = UI_LABEL_WIDTH;
        label.ae2unityIsFormLabel = true;
        label.ae2unityLabelText = labelText;
        setHelpTip(label, helpTip);
        return row;
    }

    function isPanelOwner(owner) {
        try {
            return owner instanceof Panel;
        } catch (ignoredPanelCheck) {
            return false;
        }
    }

    function openStandaloneWindow(forceCompact) {
        var standalone = buildPanel(null, {
            title: SCRIPT_NAME,
            forceCompact: forceCompact === true
        });
        standaloneWindows.push(standalone);
        resizeStandaloneWindow(standalone, forceCompact ? UI_COMPACT_WINDOW_SIZE : UI_FULL_WINDOW_SIZE);
        try {
            standalone.onClose = function () {
                removeStandaloneWindow(standalone);
            };
        } catch (ignoredStandaloneClose) {
        }
        try {
            standalone.center();
        } catch (ignoredStandaloneCenter) {
        }
        standalone.show();
        relayoutPanel(standalone);
        return standalone;
    }

    function removeStandaloneWindow(windowToRemove) {
        for (var i = standaloneWindows.length - 1; i >= 0; i--) {
            if (standaloneWindows[i] === windowToRemove) {
                standaloneWindows.splice(i, 1);
            }
        }
    }

    function setStandaloneWindowMode(panel, compact) {
        if (!panel || !panel.ae2unityIsStandaloneWindow) {
            openStandaloneWindow(compact);
            return;
        }

        panel.ae2unityForceCompact = compact === true;
        resizeStandaloneWindow(panel, compact ? UI_COMPACT_WINDOW_SIZE : UI_FULL_WINDOW_SIZE);
        relayoutPanel(panel);
    }

    function resizeStandaloneWindow(panel, size) {
        if (!panel || !size) {
            return;
        }

        var width = size[0];
        var height = size[1];
        try {
            if (panel.bounds) {
                var left = panel.bounds[0];
                var top = panel.bounds[1];
                panel.bounds = [left, top, left + width, top + height];
            }
        } catch (ignoredBoundsResize) {
        }
        try {
            panel.preferredSize = [width, height];
            panel.size = [width, height];
        } catch (ignoredSizeResize) {
        }
    }

    function setHelpTip(control, text) {
        if (!control || !text) {
            return;
        }

        try {
            control.helpTip = text;
        } catch (ignoredHelpTipError) {
        }
    }

    function compactHide(control) {
        if (control) {
            control.ae2unityHideInCompact = true;
        }
        return control;
    }

    function fixedControl(control, width, height) {
        control.alignment = ["left", "center"];
        control.preferredSize.width = width;
        control.minimumSize.width = width;
        control.ae2unityLayoutKind = "fixed";
        control.ae2unityPreferredWidth = width;
        control.ae2unityMinimumWidth = width;
        if (height) {
            control.preferredSize.height = height;
            control.minimumSize.height = height;
            control.ae2unityCompactVisibleHeight = height;
        }
        return control;
    }

    function stretchControl(control, preferredWidth, minimumWidth) {
        control.alignment = ["fill", "center"];
        control.preferredSize.width = preferredWidth;
        control.minimumSize.width = minimumWidth || 160;
        control.ae2unityLayoutKind = "stretch";
        control.ae2unityPreferredWidth = preferredWidth;
        control.ae2unityMinimumWidth = minimumWidth || 160;
        return control;
    }

    function applyCompactVisibleSize(control, compactWidth, panel) {
        if (!control) {
            return;
        }

        var height = getEffectiveCompactVisibleHeight(control, panel);
        if (height > 0) {
            try {
                control.minimumSize.height = height;
                control.preferredSize.height = height;
                control.maximumSize.height = height;
            } catch (ignoredCompactVisibleHeight) {
            }
        }

        try {
            if (compactWidth && compactWidth > 0 && !control.ae2unityPreserveCompactWidth) {
                control.alignment = ["fill", "top"];
                control.minimumSize.width = 80;
                control.preferredSize.width = compactWidth;
            }
        } catch (ignoredCompactVisibleWidth) {
        }
    }

    function getEffectiveCompactVisibleHeight(control, panel) {
        if (!control) {
            return 0;
        }

        if (control.ae2unityCompositionGroup && !isSpecificCompositionMode(panel)) {
            return UI_COMPACT_ROW_HEIGHT;
        }

        try {
            if (control.ae2unityCompactVisibleHeight && control.ae2unityCompactVisibleHeight > 0) {
                return control.ae2unityCompactVisibleHeight;
            }
        } catch (ignoredCompactVisibleHeightRead) {
        }

        return 0;
    }

    function isSpecificCompositionMode(panel) {
        try {
            var dropdown = panel ? panel.ae2unityCompSourceDropdown : null;
            return dropdown && dropdown.selection && dropdown.selection.index === 1;
        } catch (ignoredSpecificCompositionMode) {
            return false;
        }
    }

    function relayoutPanel(panel) {
        try {
            applyResponsiveLayout(panel);
            panel.layout.layout(true);
            panel.layout.resize();
        } catch (ignoredLayoutError) {
        }
    }

    function applyResponsiveLayout(panel) {
        var width = getPanelWidth(panel);
        var compact = panel.ae2unityForceCompact || (width > 0 && width < UI_COMPACT_BREAKPOINT);
        var compactWidth = Math.max(160, width - UI_COMPACT_HORIZONTAL_MARGIN);
        panel.ae2unityIsCompact = compact;

        panel.spacing = compact ? 6 : 10;
        panel.margins = compact ? UI_COMPACT_PANEL_MARGINS : UI_NORMAL_PANEL_MARGINS;

        walkControls(panel, function (control) {
            if (control.ae2unityStandaloneOnly) {
                setCompactCollapsed(control, !panel.ae2unityIsStandaloneWindow);
            }

            if (control.ae2unityHideInCompact && !control.ae2unityCompactPageItem) {
                setCompactCollapsed(control, compact);
            }

            if (control.ae2unitySpecificCompOnly) {
                setCompactCollapsed(control, compact && !isSpecificCompositionMode(panel));
            }

            if (control.ae2unityIsFormRow) {
                control.orientation = compact ? "column" : "row";
                control.alignChildren = compact ? ["fill", "top"] : ["left", "center"];
                control.spacing = compact ? 4 : 10;
                if (compact && control.ae2unityCompactVisibleHeight) {
                    applyCompactVisibleSize(control, compactWidth, panel);
                }
            }

            if (control.ae2unityIsFormLabel) {
                var hasText = control.ae2unityLabelText !== "";
                control.visible = hasText || !compact;
                control.alignment = compact ? ["fill", "top"] : ["left", "center"];
                control.minimumSize.width = compact ? 80 : UI_LABEL_WIDTH;
                control.preferredSize.width = compact ? compactWidth : UI_LABEL_WIDTH;
            }

            if (control.ae2unityLayoutKind) {
                if (compact && !control.ae2unityPreserveCompactWidth) {
                    control.alignment = ["fill", "center"];
                    control.minimumSize.width = 80;
                    control.preferredSize.width = compactWidth;
                } else if (control.ae2unityLayoutKind === "fixed") {
                    control.alignment = ["left", "center"];
                    control.minimumSize.width = control.ae2unityMinimumWidth;
                    control.preferredSize.width = control.ae2unityPreferredWidth;
                } else {
                    control.alignment = ["fill", "center"];
                    control.minimumSize.width = control.ae2unityMinimumWidth;
                    control.preferredSize.width = control.ae2unityPreferredWidth;
                }
            }

            if (control.ae2unityResultGroup) {
                control.orientation = "column";
                control.alignChildren = ["fill", "top"];
                control.alignment = ["fill", "top"];
                control.spacing = compact ? 6 : 8;
                control.minimumSize.width = compact ? 80 : UI_FIELD_WIDTH;
                control.preferredSize.width = compact ? compactWidth : UI_FIELD_WIDTH;
                control.ae2unityCompactVisibleHeight = UI_COMPACT_RESULT_GROUP_HEIGHT;
            }

            if (control.ae2unityResultStatus) {
                control.alignment = ["fill", "top"];
                control.minimumSize.width = compact ? 80 : 160;
                control.preferredSize.width = compact ? compactWidth : UI_FIELD_WIDTH;
                control.minimumSize.height = compact ? 96 : 72;
                control.preferredSize.height = compact ? UI_COMPACT_RESULT_STATUS_HEIGHT : 88;
                control.maximumSize.height = compact ? UI_COMPACT_RESULT_STATUS_HEIGHT : UI_MAX_VISIBLE_HEIGHT;
                control.ae2unityCompactVisibleHeight = UI_COMPACT_RESULT_STATUS_HEIGHT;
            }
        });

        updateStandaloneControls(panel, compact);
        applyCompactPages(panel, compact);
    }

    function updateStandaloneControls(panel, compact) {
        var controls = panel.ae2unityStandaloneControls;
        if (!controls) {
            return;
        }

        setCompactCollapsed(controls.group, !panel.ae2unityIsStandaloneWindow);
        try {
            controls.compactButton.enabled = panel.ae2unityIsStandaloneWindow && !compact;
            controls.fullButton.enabled = panel.ae2unityIsStandaloneWindow && compact;
        } catch (ignoredStandaloneEnabledState) {
        }
    }

    function setCompactPages(panel, pages) {
        var pager = panel.ae2unityCompactPager;
        if (!pager) {
            return;
        }

        pager.pages = pages || [];
        for (var i = 0; i < pager.pages.length; i++) {
            var controls = pager.pages[i].controls || [];
            for (var j = 0; j < controls.length; j++) {
                if (controls[j]) {
                    controls[j].ae2unityCompactPageItem = true;
                }
            }
        }
    }

    function applyCompactPages(panel, compact) {
        var pager = panel.ae2unityCompactPager;
        if (!pager) {
            return;
        }

        var pages = pager.pages || [];
        setCompactCollapsed(pager.group, !compact);
        if (!compact) {
            forEachCompactPageControl(pages, function (control) {
                setCompactCollapsed(control, false);
            });
            return;
        }

        var pageCount = pages.length;
        if (pageCount <= 0) {
            return;
        }

        var compactWidth = Math.max(160, getPanelWidth(panel) - UI_COMPACT_HORIZONTAL_MARGIN);
        pager.pageIndex = clampInteger(pager.pageIndex, 0, pageCount - 1);
        for (var i = 0; i < pages.length; i++) {
            var controls = pages[i].controls || [];
            for (var j = 0; j < controls.length; j++) {
                var isCollapsed = i !== pager.pageIndex;
                setCompactCollapsed(controls[j], isCollapsed);
                if (!isCollapsed) {
                    applyCompactVisibleSize(controls[j], compactWidth, panel);
                }
            }
        }

        try {
            pager.pageText.text = (pager.pageIndex + 1) + "/" + pageCount + " " + pages[pager.pageIndex].title;
        } catch (ignoredCompactPageLabel) {
        }

        try {
            pager.prevButton.enabled = pager.pageIndex > 0;
            pager.nextButton.enabled = pager.pageIndex < pageCount - 1;
        } catch (ignoredCompactPageButtons) {
        }

        try {
            setCompactCollapsed(pager.runButton, pages[pager.pageIndex].title !== "Export");
        } catch (ignoredCompactRunButton) {
        }

        try {
            pager.scrollbar.minvalue = 0;
            pager.scrollbar.maxvalue = Math.max(0, pageCount - 1);
            pager.scrollbar.stepdelta = 1;
            pager.scrollbar.jumpdelta = 1;
            pager.scrollbar.value = pager.pageIndex;
            pager.scrollbar.enabled = pageCount > 1;
        } catch (ignoredCompactPageScrollbar) {
        }
    }

    function forEachCompactPageControl(pages, callback) {
        var seen = [];
        for (var i = 0; i < pages.length; i++) {
            var controls = pages[i].controls || [];
            for (var j = 0; j < controls.length; j++) {
                var control = controls[j];
                if (!control || containsControl(seen, control)) {
                    continue;
                }
                seen.push(control);
                callback(control);
            }
        }
    }

    function containsControl(controls, target) {
        for (var i = 0; i < controls.length; i++) {
            if (controls[i] === target) {
                return true;
            }
        }

        return false;
    }

    function setCompactPage(panel, pageIndex) {
        var pager = panel.ae2unityCompactPager;
        if (!pager || !panel.ae2unityIsCompact) {
            return false;
        }

        var pageCount = pager.pages ? pager.pages.length : 0;
        if (pageCount <= 0) {
            return false;
        }

        var nextIndex = clampInteger(pageIndex, 0, pageCount - 1);
        if (nextIndex === pager.pageIndex) {
            applyCompactPages(panel, true);
            relayoutPanel(panel);
            return false;
        }

        pager.pageIndex = nextIndex;
        applyCompactPages(panel, true);
        relayoutPanel(panel);
        return true;
    }

    function setCompactPageByTitle(panel, title) {
        var pager = panel ? panel.ae2unityCompactPager : null;
        if (!pager || !pager.pages || !panel.ae2unityIsCompact) {
            return false;
        }

        for (var i = 0; i < pager.pages.length; i++) {
            if (pager.pages[i] && pager.pages[i].title === title) {
                return setCompactPage(panel, i);
            }
        }

        return false;
    }

    function showStatusResult(panel, statusControl, message) {
        if (statusControl) {
            statusControl.text = message || "";
        }

        setCompactPageByTitle(panel, "Result");
        relayoutPanel(panel);
        flushPanelUi(panel);
    }

    function flushPanelUi(panel) {
        try {
            if (panel && panel.update) {
                panel.update();
            }
        } catch (ignoredPanelUpdate) {
        }

        try {
            app.refresh();
        } catch (ignoredAppRefresh) {
        }
    }

    function advanceCompactPage(panel, direction) {
        var pager = panel.ae2unityCompactPager;
        if (!pager || !panel.ae2unityIsCompact) {
            return false;
        }

        var step = direction < 0 ? -1 : 1;
        return setCompactPage(panel, pager.pageIndex + step);
    }

    function setCompactCollapsed(control, collapsed) {
        if (!control) {
            return;
        }

        if (!control.ae2unityStoredVerticalSize) {
            control.ae2unityStoredVerticalSize = {
                preferred: readSizeValue(control.preferredSize, 1),
                minimum: readSizeValue(control.minimumSize, 1),
                maximum: readSizeValue(control.maximumSize, 1)
            };
        }

        control.visible = !collapsed;
        try {
            if (collapsed) {
                control.minimumSize.height = 0;
                control.preferredSize.height = 0;
                control.maximumSize.height = 0;
            } else {
                var fallbackHeight = getCompactVisibleHeight(control);
                restoreHeight(control.minimumSize, control.ae2unityStoredVerticalSize.minimum, 0, false);
                restoreHeight(control.preferredSize, control.ae2unityStoredVerticalSize.preferred, fallbackHeight, false);
                restoreHeight(control.maximumSize, control.ae2unityStoredVerticalSize.maximum, UI_MAX_VISIBLE_HEIGHT, true);
            }
        } catch (ignoredCollapseSize) {
        }
    }

    function getCompactVisibleHeight(control) {
        if (!control) {
            return 0;
        }

        try {
            if (control.ae2unityCompactVisibleHeight && control.ae2unityCompactVisibleHeight > 0) {
                return control.ae2unityCompactVisibleHeight;
            }
        } catch (ignoredCompactHeight) {
        }

        try {
            var preferred = readSizeValue(control.preferredSize, 1);
            if (preferred && preferred > 0) {
                return preferred;
            }
        } catch (ignoredPreferredCompactHeight) {
        }

        return 0;
    }

    function clampInteger(value, minValue, maxValue) {
        var numberValue = Math.round(Number(value) || 0);
        return Math.max(minValue, Math.min(maxValue, numberValue));
    }

    function readSizeValue(size, index) {
        try {
            if (size && typeof size.length === "number" && size.length > index) {
                return Number(size[index]);
            }
        } catch (ignoredReadSize) {
        }

        try {
            if (size && typeof size.height !== "undefined") {
                return Number(size.height);
            }
        } catch (ignoredReadSizeHeight) {
        }

        return 0;
    }

    function restoreHeight(size, value, fallback, isMaximumSize) {
        if (!size) {
            return;
        }

        if (value && value > 0) {
            size.height = value;
        } else if (fallback && fallback > 0) {
            size.height = fallback;
        } else if (isMaximumSize) {
            size.height = UI_MAX_VISIBLE_HEIGHT;
        } else {
            size.height = -1;
        }
    }

    function bindCompactMouseWheelTree(control, panel) {
        walkControls(control, function (child) {
            bindCompactMouseWheel(child, panel);
        });
    }

    function bindStandaloneShortcutTree(control, panel) {
        walkControls(control, function (child) {
            bindStandaloneShortcut(child, panel);
        });
    }

    function bindStandaloneShortcut(control, panel) {
        if (!control || !control.addEventListener) {
            return;
        }

        var handler = function (event) {
            var compactDirection = getCompactPageKeyDirection(event, panel);
            if (compactDirection !== 0 && advanceCompactPage(panel, compactDirection)) {
                consumeEvent(event);
                return;
            }

            if (!panel || !panel.ae2unityIsStandaloneWindow) {
                return;
            }

            if (matchesShortcut(event, "C", 67)) {
                setStandaloneWindowMode(panel, true);
                consumeEvent(event);
                return;
            }

            if (matchesShortcut(event, "F", 70)) {
                setStandaloneWindowMode(panel, false);
                consumeEvent(event);
            }
        };

        try {
            control.addEventListener("keydown", handler);
        } catch (ignoredKeydownBind) {
        }
        try {
            control.addEventListener("KeyDown", handler);
        } catch (ignoredKeyDownBind) {
        }
    }

    function getCompactPageKeyDirection(event, panel) {
        if (!event || !panel || !panel.ae2unityIsCompact || !eventLooksPlain(event)) {
            return 0;
        }

        var keyName = readKeyName(event);
        if (keyName === "UP" || keyName === "ARROWUP" || keyName === "LEFT" || keyName === "ARROWLEFT" || keyName === "PAGEUP" || keyName === "PGUP") {
            return -1;
        }
        if (keyName === "DOWN" || keyName === "ARROWDOWN" || keyName === "RIGHT" || keyName === "ARROWRIGHT" || keyName === "PAGEDOWN" || keyName === "PGDN") {
            return 1;
        }

        try {
            if (event.keyCode === 38 || event.keyCode === 37 || event.keyCode === 33) {
                return -1;
            }
            if (event.keyCode === 40 || event.keyCode === 39 || event.keyCode === 34) {
                return 1;
            }
        } catch (ignoredCompactKeyCode) {
        }

        return 0;
    }

    function eventLooksPlain(event) {
        try {
            if (event.ctrlKey || event.metaKey || event.commandKey || event.altKey) {
                return false;
            }
        } catch (ignoredPlainEventCheck) {
        }

        return true;
    }

    function matchesShortcut(event, keyCharacter, keyCode) {
        if (!event) {
            return false;
        }

        var hasModifier = false;
        try {
            hasModifier = event.ctrlKey || event.metaKey || event.commandKey;
        } catch (ignoredModifierCheck) {
            hasModifier = false;
        }
        if (!hasModifier || !event.shiftKey) {
            return false;
        }

        try {
            if (event.keyCode === keyCode) {
                return true;
            }
        } catch (ignoredKeyCodeCheck) {
        }

        var keyName = readKeyName(event);

        return keyName === keyCharacter || keyName === ("U+00" + keyCode.toString(16).toUpperCase());
    }

    function readKeyName(event) {
        try {
            return String(event.keyName || event.keyIdentifier || event.key || "")
                .replace(/\s+/g, "")
                .toUpperCase();
        } catch (ignoredKeyNameRead) {
            return "";
        }
    }

    function consumeEvent(event) {
        if (!event) {
            return;
        }

        try {
            event.preventDefault();
        } catch (ignoredPreventDefault) {
        }
        try {
            event.stopPropagation();
        } catch (ignoredStopPropagation) {
        }
    }

    function bindCompactMouseWheel(control, panel) {
        if (!control || !control.addEventListener) {
            return;
        }

        var activateHandler = function (event) {
            activateCompactScroll(panel);
            if (isMiddleMouseEvent(event)) {
                try {
                    event.preventDefault();
                } catch (ignoredMiddlePreventDefault) {
                }
            }
        };

        var handler = function (event) {
            if (event && event.ae2unityScrollHandled) {
                return;
            }

            activateCompactScroll(panel);
            var direction = getWheelDirection(event);
            if (direction === 0 || !advanceCompactPage(panel, direction)) {
                return;
            }

            try {
                event.ae2unityScrollHandled = true;
            } catch (ignoredHandledFlag) {
            }
            try {
                event.preventDefault();
            } catch (ignoredPreventDefault) {
            }
            try {
                event.stopPropagation();
            } catch (ignoredStopPropagation) {
            }
        };

        var events = ["mousewheel", "MouseWheel", "wheel", "scroll", "Scroll"];
        for (var i = 0; i < events.length; i++) {
            try {
                control.addEventListener(events[i], handler);
            } catch (ignoredWheelBind) {
            }
        }

        var activationEvents = ["mousedown", "MouseDown", "click", "Click"];
        for (var j = 0; j < activationEvents.length; j++) {
            try {
                control.addEventListener(activationEvents[j], activateHandler);
            } catch (ignoredActivationBind) {
            }
        }

        try {
            control.onMouseWheel = handler;
        } catch (ignoredOnMouseWheel) {
        }
    }

    function activateCompactScroll(panel) {
        if (!panel || !panel.ae2unityIsCompact) {
            return;
        }

        panel.ae2unityCompactActive = true;
        try {
            panel.active = true;
        } catch (ignoredPanelActivate) {
        }
    }

    function isMiddleMouseEvent(event) {
        if (!event) {
            return false;
        }

        try {
            if (event.button === 1 || event.which === 2) {
                return true;
            }
        } catch (ignoredButtonCheck) {
        }

        return false;
    }

    function getWheelDirection(event) {
        if (!event) {
            return 0;
        }

        if (typeof event.deltaY === "number" && event.deltaY !== 0) {
            return event.deltaY > 0 ? 1 : -1;
        }
        if (typeof event.wheelDelta === "number" && event.wheelDelta !== 0) {
            return event.wheelDelta < 0 ? 1 : -1;
        }
        if (typeof event.detail === "number" && event.detail !== 0) {
            return event.detail > 0 ? 1 : -1;
        }

        return 0;
    }

    function getPanelWidth(panel) {
        try {
            if (panel.size && panel.size.length > 0 && panel.size[0] > 0) {
                return panel.size[0];
            }
        } catch (ignoredSize) {
        }

        try {
            if (panel.bounds) {
                return panel.bounds[2] - panel.bounds[0];
            }
        } catch (ignoredBounds) {
        }

        return 820;
    }

    function walkControls(control, callback) {
        callback(control);
        if (!control.children) {
            return;
        }

        for (var i = 0; i < control.children.length; i++) {
            walkControls(control.children[i], callback);
        }
    }

    function runConfiguredExport(config) {
        var messages = [];
        var mode = config.exportMode || "bridge";

        if (mode === "bridge" || mode === "bridgeMedia") {
            var bridgeResult = sendActiveCompToUnityBridge(
                config.projectPath,
                config.unityExportPath,
                {
                    comp: config.comp,
                    exportReferenceFrames: config.exportReferenceFrames,
                    exportBakedFrames: config.exportBakedFrames,
                    generateShaderAndMaterial: config.generateShaderAndMaterial
                });
            messages.push("Bridge job sent: " + bridgeResult.jobId);
        }

        if (mode === "media" || mode === "bridgeMedia") {
            var mediaResult = queueMediaEncoderExport(
                config.comp,
                config.projectPath,
                config.mediaExportPath,
                {
                    extension: config.mediaExtension,
                    outputModuleTemplate: config.outputModuleTemplate,
                    startMediaEncoder: config.startMediaEncoder
                });
            messages.push("Media queued: " + mediaResult.fsName);
        }

        if (mode === "direct") {
            var directResult = exportActiveCompToUnityProject(
                config.projectPath,
                config.unityExportPath,
                {
                    comp: config.comp,
                    exportReferenceFrames: config.exportReferenceFrames,
                    exportBakedFrames: config.exportBakedFrames
                });
            messages.push("Exported: " + directResult.fsName);
        }

        if (mode === "folder") {
            var folderResult = exportActiveCompToChosenFolder({
                comp: config.comp,
                exportReferenceFrames: config.exportReferenceFrames,
                exportBakedFrames: config.exportBakedFrames
            });
            messages.push("Exported: " + folderResult.fsName);
        }

        return messages.join(" | ");
    }

    function getExportModeSelection(value) {
        if (value === "bridgeMedia") {
            return 1;
        }
        if (value === "media") {
            return 2;
        }
        if (value === "direct") {
            return 3;
        }
        if (value === "folder") {
            return 4;
        }

        return 0;
    }

    function getExportModeValue(dropdown) {
        if (!dropdown.selection) {
            return "bridge";
        }

        switch (dropdown.selection.index) {
            case 1:
                return "bridgeMedia";
            case 2:
                return "media";
            case 3:
                return "direct";
            case 4:
                return "folder";
            default:
                return "bridge";
        }
    }

    function updateMediaControls(exportModeDropdown, mediaFolderGroup, mediaOptionsGroup) {
        var mode = getExportModeValue(exportModeDropdown);
        var enabled = mode === "media" || mode === "bridgeMedia";
        mediaFolderGroup.enabled = enabled;
        mediaOptionsGroup.enabled = enabled;
    }

    function getDropdownSelectionByText(dropdown, text) {
        for (var i = 0; i < dropdown.items.length; i++) {
            if (dropdown.items[i].text === text) {
                return dropdown.items[i];
            }
        }

        return dropdown.items.length > 0 ? dropdown.items[0] : null;
    }

    function refreshCompositionDropdown(dropdown, status) {
        var savedIndex = Number(loadSetting("SpecificCompIndex", "0"));
        var comps = loadCompositions();
        while (dropdown.items.length > 0) {
            dropdown.remove(dropdown.items[0]);
        }

        if (comps.length === 0) {
            var emptyItem = dropdown.add("item", "No compositions found");
            emptyItem.projectItemIndex = 0;
            dropdown.selection = emptyItem;
            status.text = "No compositions found in current AE project.";
            return;
        }

        var selectedIndex = 0;
        for (var i = 0; i < comps.length; i++) {
            var label = comps[i].name + "  (" + comps[i].width + "x" + comps[i].height + ", " + formatNumber(comps[i].duration) + "s)";
            var item = dropdown.add("item", label);
            item.projectItemIndex = comps[i].projectItemIndex;
            if (comps[i].projectItemIndex === savedIndex) {
                selectedIndex = i;
            }
        }

        dropdown.selection = dropdown.items[selectedIndex];
    }

    function loadCompositions() {
        var comps = [];
        if (!app.project) {
            return comps;
        }

        for (var i = 1; i <= app.project.numItems; i++) {
            var item = app.project.item(i);
            if (item instanceof CompItem) {
                comps.push({
                    projectItemIndex: i,
                    name: item.name,
                    width: item.width,
                    height: item.height,
                    duration: item.duration
                });
            }
        }

        return comps;
    }

    function refreshOutputTemplateDropdown(dropdown, compSourceDropdown, compDropdown, status) {
        var savedTemplate = loadSetting("MediaOutputModuleTemplate", "");
        var templates = [];
        try {
            templates = getOutputModuleTemplates(getSelectedComposition(compSourceDropdown, compDropdown));
        } catch (ignoredTemplateLoad) {
            templates = [];
        }

        while (dropdown.items.length > 0) {
            dropdown.remove(dropdown.items[0]);
        }

        if (templates.length === 0) {
            templates = ["Lossless"];
        }

        var selectedIndex = 0;
        for (var i = 0; i < templates.length; i++) {
            var item = dropdown.add("item", templates[i]);
            if (templates[i] === savedTemplate) {
                selectedIndex = i;
            }
        }

        dropdown.selection = dropdown.items[selectedIndex];
        if (dropdown.selection) {
            saveSetting("MediaOutputModuleTemplate", dropdown.selection.text);
        }
    }

    function getOutputModuleTemplates(comp) {
        var templates = [];
        var rqItem = null;
        app.beginUndoGroup("AE2Unity Read Output Templates");
        try {
            rqItem = app.project.renderQueue.items.add(comp);
            var outputModule = rqItem.outputModule(1);
            for (var i = 0; i < outputModule.templates.length; i++) {
                templates.push(outputModule.templates[i]);
            }
            rqItem.remove();
        } finally {
            try {
                if (rqItem) {
                    rqItem.remove();
                }
            } catch (ignoredRemove) {
            }
            app.endUndoGroup();
        }

        return templates;
    }

    function getSelectedComposition(compSourceDropdown, compDropdown) {
        if (!compSourceDropdown.selection || compSourceDropdown.selection.index === 0) {
            return getActiveComp();
        }

        if (!compDropdown.selection || !compDropdown.selection.projectItemIndex) {
            throw new Error("Choose a specific composition.");
        }

        var comp = app.project.item(Number(compDropdown.selection.projectItemIndex));
        if (!(comp instanceof CompItem)) {
            throw new Error("Selected project item is not a composition.");
        }

        return comp;
    }

    function getCompositionFromOptions(options) {
        if (options && options.comp) {
            return options.comp;
        }

        return getActiveComp();
    }

    function formatNumber(value) {
        return Math.round(Number(value) * 100) / 100;
    }

    function refreshProjectDropdown(dropdown, projectPathText, status) {
        var savedPath = projectPathText.text || loadSetting("UnityProjectPath", "");
        var projects = loadUnityHubProjects();
        while (dropdown.items.length > 0) {
            dropdown.remove(dropdown.items[0]);
        }

        if (projects.length === 0) {
            var emptyItem = dropdown.add("item", "No Unity Hub projects found");
            emptyItem.projectPath = "";
            dropdown.selection = emptyItem;
            status.text = "No Unity Hub projects found. Use Choose.";
            return;
        }

        var selectedIndex = -1;
        for (var i = 0; i < projects.length; i++) {
            var label = projects[i].title;
            if (projects[i].version) {
                label += "  [" + projects[i].version + "]";
            }
            if (!projects[i].exists) {
                label += "  (missing)";
            }

            var item = dropdown.add("item", label);
            item.projectPath = projects[i].path;
            item.projectTitle = projects[i].title;
            if (normalizePathForCompare(projects[i].path) === normalizePathForCompare(savedPath)) {
                selectedIndex = i;
            }
        }

        if (selectedIndex < 0) {
            selectedIndex = findCurrentWorkingProjectIndex(projects);
        }

        if (selectedIndex < 0) {
            selectedIndex = 0;
        }

        dropdown.selection = dropdown.items[selectedIndex];
        if (dropdown.selection && dropdown.selection.projectPath) {
            projectPathText.text = dropdown.selection.projectPath;
            saveSetting("UnityProjectPath", projectPathText.text);
        }

        status.text = "Loaded " + projects.length + " Unity Hub projects.";
    }

    function findCurrentWorkingProjectIndex(projects) {
        var guessed = "";
        try {
            guessed = new Folder(".").fsName;
        } catch (ignoredCurrentFolder) {
            guessed = "";
        }

        for (var i = 0; i < projects.length; i++) {
            if (normalizePathForCompare(projects[i].path) === normalizePathForCompare(guessed)) {
                return i;
            }
        }

        return -1;
    }

    function loadUnityHubProjects() {
        var projectsFile = findExistingFile(getUnityHubFileCandidates("projects-v1.json"));
        if (!projectsFile) {
            return [];
        }

        var text = readTextFile(projectsFile);
        var parsed = parseJson(text);
        var data = parsed && parsed.data ? parsed.data : parsed;
        var projects = [];

        for (var key in data) {
            if (!data.hasOwnProperty(key)) {
                continue;
            }

            var source = data[key];
            if (!source) {
                continue;
            }

            var path = source.path || key;
            var title = source.title || source.projectName || baseName(path);
            if (!path || !title) {
                continue;
            }

            projects.push({
                title: title,
                path: path,
                version: source.version || "",
                lastModified: Number(source.lastModified || 0),
                sizeInBytes: Number(source.sizeInBytes || 0),
                isFavorite: source.isFavorite === true,
                exists: isUnityProjectFolder(new Folder(path))
            });
        }

        projects = applyUnityHubSort(projects);
        return projects;
    }

    function applyUnityHubSort(projects) {
        var sortFile = findExistingFile(getUnityHubFileCandidates("projectSortPreferences.json"));
        var sortBy = "lastModified";
        var sortDirection = "descending";

        if (sortFile) {
            try {
                var sort = parseJson(readTextFile(sortFile));
                sortBy = sort.sortBy || sortBy;
                sortDirection = sort.sortDirection || sortDirection;
            } catch (ignoredSort) {
            }
        }

        projects.sort(function (a, b) {
            var direction = sortDirection === "ascending" ? 1 : -1;
            var av = projectSortValue(a, sortBy);
            var bv = projectSortValue(b, sortBy);

            if (typeof av === "number" && typeof bv === "number") {
                if (av === bv) {
                    return compareText(a.title, b.title);
                }
                return av < bv ? -direction : direction;
            }

            return compareText(String(av), String(bv)) * direction;
        });

        return projects;
    }

    function projectSortValue(project, sortBy) {
        if (sortBy === "title" || sortBy === "name" || sortBy === "projectName") {
            return project.title.toLowerCase();
        }
        if (sortBy === "path") {
            return project.path.toLowerCase();
        }
        if (sortBy === "version") {
            return project.version.toLowerCase();
        }
        if (sortBy === "sizeInBytes") {
            return project.sizeInBytes;
        }
        if (sortBy === "isFavorite") {
            return project.isFavorite ? 1 : 0;
        }

        return project.lastModified;
    }

    function compareText(a, b) {
        if (a === b) {
            return 0;
        }

        return a < b ? -1 : 1;
    }

    function getUnityHubFileCandidates(fileName) {
        var candidates = [];
        var home = Folder("~").fsName;
        var appData = safeEnv("APPDATA");
        var localAppData = safeEnv("LOCALAPPDATA");

        candidates.push(home + "/Library/Application Support/UnityHub/" + fileName);
        candidates.push(home + "/Library/Application Support/Unity Hub/" + fileName);

        if (appData) {
            candidates.push(appData + "/UnityHub/" + fileName);
            candidates.push(appData + "/Unity Hub/" + fileName);
        }
        if (localAppData) {
            candidates.push(localAppData + "/UnityHub/" + fileName);
            candidates.push(localAppData + "/Unity Hub/" + fileName);
        }

        return candidates;
    }

    function findExistingFile(paths) {
        for (var i = 0; i < paths.length; i++) {
            var file = new File(paths[i]);
            if (file.exists) {
                return file;
            }
        }

        return null;
    }

    function readTextFile(file) {
        file.encoding = "UTF-8";
        if (!file.open("r")) {
            throw new Error("Could not read file: " + file.fsName);
        }

        var text = file.read();
        file.close();
        return text;
    }

    function parseJson(text) {
        if (typeof JSON !== "undefined" && JSON.parse) {
            return JSON.parse(text);
        }

        return eval("(" + text + ")");
    }

    function safeEnv(name) {
        try {
            return $.getenv(name) || "";
        } catch (ignoredEnv) {
            return "";
        }
    }

    function baseName(path) {
        var parts = String(path).replace(/\\/g, "/").split("/");
        return parts.length > 0 ? parts[parts.length - 1] : String(path);
    }

    function normalizePathForCompare(path) {
        return String(path || "").replace(/\\/g, "/").replace(/\/+$/, "").toLowerCase();
    }

    function exportActiveCompToChosenFolder(options) {
        var comp = getCompositionFromOptions(options);
        var folder = Folder.selectDialog("Choose export folder for " + comp.name);
        if (!folder) {
            throw new Error("Export cancelled.");
        }

        var exportFolder = getCompositionOutputFolder(folder, comp);
        ensureFolderExists(exportFolder);
        return exportActiveCompToFolder(comp, exportFolder, options);
    }

    function exportActiveCompToUnityProject(projectPath, relativePath, options) {
        var comp = getCompositionFromOptions(options);
        var projectFolder = new Folder(projectPath);
        if (!isUnityProjectFolder(projectFolder)) {
            throw new Error("Choose a Unity project folder that contains Assets and ProjectSettings.");
        }

        var normalizedRelativePath = normalizeRelativePath(relativePath || DEFAULT_UNITY_EXPORT_RELATIVE_PATH);
        if (!(normalizedRelativePath === "Assets" || normalizedRelativePath.indexOf("Assets/") === 0)) {
            normalizedRelativePath = "Assets/" + normalizedRelativePath;
        }

        saveSetting("UnityProjectPath", projectFolder.fsName);
        saveSetting("UnityExportRelativePath", normalizedRelativePath);

        var exportFolder = getCompositionOutputFolder(new Folder(joinPath(projectFolder.fsName, normalizedRelativePath)), comp);
        ensureFolderExists(exportFolder);
        return exportActiveCompToFolder(comp, exportFolder, options);
    }

    function sendActiveCompToUnityBridge(projectPath, relativePath, options) {
        var comp = getCompositionFromOptions(options);
        var projectFolder = new Folder(projectPath);
        if (!isUnityProjectFolder(projectFolder)) {
            throw new Error("Choose a Unity project folder that contains Assets and ProjectSettings.");
        }

        var normalizedRelativePath = normalizeRelativePath(relativePath || DEFAULT_UNITY_EXPORT_RELATIVE_PATH);
        if (!(normalizedRelativePath === "Assets" || normalizedRelativePath.indexOf("Assets/") === 0)) {
            normalizedRelativePath = "Assets/" + normalizedRelativePath;
        }

        saveSetting("UnityProjectPath", projectFolder.fsName);
        saveSetting("UnityExportRelativePath", normalizedRelativePath);

        var bridgeRoot = new Folder(projectFolder.fsName + "/" + BRIDGE_FOLDER_NAME);
        var inboxFolder = new Folder(bridgeRoot.fsName + "/inbox");
        var payloadsFolder = new Folder(bridgeRoot.fsName + "/payloads");
        var outboxFolder = new Folder(bridgeRoot.fsName + "/outbox");
        ensureFolderExists(inboxFolder);
        ensureFolderExists(payloadsFolder);
        ensureFolderExists(outboxFolder);

        var jobId = createJobId(comp);
        var payloadFolder = new Folder(payloadsFolder.fsName + "/" + jobId);
        ensureFolderExists(payloadFolder);

        var payloadFile = exportActiveCompToFolder(comp, payloadFolder, options);
        var job = {
            protocolVersion: "0.1.0",
            jobId: jobId,
            command: "ImportAe2Shader",
            createdAt: new Date().toUTCString(),
            sender: "After Effects 2026",
            compName: comp.name,
            payloadFile: "payloads/" + jobId + "/" + payloadFile.name,
            referenceFramesFolder: options && options.exportReferenceFrames === false ? "" : "payloads/" + jobId + "/reference_frames",
            unityOutputPath: normalizedRelativePath,
            overwriteGeneratedAssets: true,
            generateShaderAndMaterial: !(options && options.generateShaderAndMaterial === false),
            refreshAssetDatabase: true
        };

        var tempJobFile = new File(inboxFolder.fsName + "/" + jobId + ".tmp");
        writeTextFile(tempJobFile, toJson(job));
        if (!tempJobFile.rename(jobId + ".job")) {
            var jobFile = new File(inboxFolder.fsName + "/" + jobId + ".job");
            writeTextFile(jobFile, toJson(job));
            try {
                tempJobFile.remove();
            } catch (ignoredRemove) {
            }
        }

        saveSetting("LastBridgeJobId", jobId);
        return {
            jobId: jobId,
            payloadFile: payloadFile
        };
    }

    function queueMediaEncoderExport(comp, projectPath, mediaRelativePath, options) {
        options = options || {};
        var projectFolder = new Folder(projectPath);
        if (!isUnityProjectFolder(projectFolder)) {
            throw new Error("Choose a Unity project folder that contains Assets and ProjectSettings.");
        }

        var normalizedMediaPath = normalizeRelativePath(mediaRelativePath || DEFAULT_MEDIA_EXPORT_RELATIVE_PATH);
        if (!(normalizedMediaPath === "Assets" || normalizedMediaPath.indexOf("Assets/") === 0)) {
            normalizedMediaPath = "Assets/" + normalizedMediaPath;
        }

        saveSetting("MediaExportRelativePath", normalizedMediaPath);

        var mediaFolder = new Folder(joinPath(projectFolder.fsName, normalizedMediaPath));
        ensureFolderExists(mediaFolder);

        var extension = normalizeExtension(options.extension || "mp4");
        var outputFile = new File(mediaFolder.fsName + "/" + sanitizeFileName(comp.name) + "." + extension);
        var existingItems = collectRenderQueueItems();
        var previousStates = setRenderQueueItemsRender(existingItems, false);
        var rqItem = null;

        app.beginUndoGroup("AE2Unity Queue Media Encoder Export");
        try {
            rqItem = app.project.renderQueue.items.add(comp);
            rqItem.render = true;

            var outputModule = rqItem.outputModule(1);
            if (options.outputModuleTemplate) {
                try {
                    outputModule.applyTemplate(options.outputModuleTemplate);
                } catch (ignoredTemplate) {
                }
            }

            outputModule.file = outputFile;

            if (!app.project.renderQueue.canQueueInAME) {
                throw new Error("After Effects cannot queue the current render item in Adobe Media Encoder.");
            }

            app.project.renderQueue.queueInAME(options.startMediaEncoder !== false);
        } finally {
            restoreRenderQueueItemsRender(previousStates);
            app.endUndoGroup();
        }

        return outputFile;
    }

    function collectRenderQueueItems() {
        var items = [];
        try {
            for (var i = 1; i <= app.project.renderQueue.numItems; i++) {
                items.push(app.project.renderQueue.item(i));
            }
        } catch (ignoredCollect) {
        }

        return items;
    }

    function setRenderQueueItemsRender(items, renderValue) {
        var states = [];
        for (var i = 0; i < items.length; i++) {
            try {
                states.push({
                    item: items[i],
                    render: items[i].render
                });
                items[i].render = renderValue;
            } catch (ignoredState) {
            }
        }

        return states;
    }

    function restoreRenderQueueItemsRender(states) {
        for (var i = 0; i < states.length; i++) {
            try {
                states[i].item.render = states[i].render;
            } catch (ignoredRestore) {
            }
        }
    }

    function readLastBridgeResult(projectPath) {
        var projectFolder = new Folder(projectPath);
        if (!isUnityProjectFolder(projectFolder)) {
            throw new Error("Choose a Unity project folder first.");
        }

        var jobId = loadSetting("LastBridgeJobId", "");
        if (!jobId) {
            return "No bridge job has been sent yet.";
        }

        var resultFile = new File(projectFolder.fsName + "/" + BRIDGE_FOLDER_NAME + "/outbox/" + jobId + ".result.json");
        if (!resultFile.exists) {
            return "Waiting for Unity AEBridge: " + jobId;
        }

        resultFile.encoding = "UTF-8";
        if (!resultFile.open("r")) {
            throw new Error("Could not read bridge result: " + resultFile.fsName);
        }

        var text = resultFile.read();
        resultFile.close();
        return compactResultText(text);
    }

    function getActiveComp() {
        if (!app.project) {
            throw new Error("No active After Effects project.");
        }

        var comp = app.project.activeItem;
        if (!(comp instanceof CompItem)) {
            throw new Error("Select or open a composition before exporting.");
        }

        return comp;
    }

    function exportActiveCompToFolder(comp, folder, options) {
        options = options || {};
        ensureFolderExists(folder);

        var assetMap = {};
        var assets = [];
        var warnings = [];
        var layers = [];
        var prepared = prepareCompositionForExport(comp, warnings);
        var exportComp = prepared.comp;
        var referenceFolder = new Folder(folder.fsName + "/reference_frames");
        var bakedFrames = {
            enabled: false,
            relativePath: "",
            filePrefix: "",
            fileExtension: "png",
            frameCount: 0,
            width: comp.width,
            height: comp.height,
            frameRate: comp.frameRate,
            duration: comp.duration,
            startTime: 0,
            hasAlpha: true
        };
        var vectorAnimation = {
            enabled: false,
            vectorOnly: false,
            frameCount: 0,
            frameRate: comp.frameRate,
            duration: comp.duration,
            primitives: []
        };

        app.beginUndoGroup(SCRIPT_NAME);
        try {
            if (options.exportReferenceFrames !== false) {
                ensureFolderExists(referenceFolder);
            }

            for (var i = 1; i <= exportComp.numLayers; i++) {
                layers.push(collectLayer(exportComp.layer(i), assetMap, assets, warnings));
            }

            vectorAnimation = collectVectorAnimation(exportComp, warnings, prepared);

            if (options.exportReferenceFrames !== false) {
                saveReferenceFrames(exportComp, referenceFolder, warnings);
            }

            if (options.exportBakedFrames !== false && !(vectorAnimation.enabled && vectorAnimation.vectorOnly)) {
                bakedFrames = saveBakedFrames(exportComp, folder, warnings);
            }
        } finally {
            prepared.cleanup();
            app.endUndoGroup();
        }

        var document = {
            schemaVersion: SCHEMA_VERSION,
            exporter: "AE2Unity exporter 0.5.1",
            exportedAt: new Date().toUTCString(),
            comp: collectComp(comp),
            layers: layers,
            assets: assets,
            vectorAnimation: vectorAnimation,
            bakedFrames: bakedFrames,
            warnings: warnings
        };

        var outputFile = new File(folder.fsName + "/" + sanitizeFileName(comp.name) + ".ae2shader");
        outputFile.encoding = "UTF-8";
        if (!outputFile.open("w")) {
            throw new Error("Could not write export file: " + outputFile.fsName);
        }

        outputFile.write(toJson(document));
        outputFile.close();
        return outputFile;
    }

    function getCompositionOutputFolder(baseFolder, comp) {
        var safeName = sanitizeFileName(comp && comp.name ? comp.name : "Untitled");
        var normalizedBase = normalizePathForCompare(baseFolder.fsName);
        var normalizedName = safeName.toLowerCase();
        if (baseName(normalizedBase) === normalizedName) {
            return baseFolder;
        }

        return new Folder(baseFolder.fsName + "/" + safeName);
    }

    function prepareCompositionForExport(comp, warnings) {
        var hadText = compositionHasTextRecursive(comp, {});
        if (!hadText) {
            return {
                comp: comp,
                hadText: false,
                cleanup: function () {
                }
            };
        }

        var registry = {};
        var duplicates = [];
        var rootDuplicate = duplicateCompositionGraph(comp, registry, duplicates, warnings);
        convertTextLayersToShapesRecursive(rootDuplicate, {}, warnings);
        warnings.push({
            code: "TEXT_CONVERTED_TO_SHAPES",
            message: "Text layers were converted to temporary shape paths before export. The original After Effects project was not modified.",
            layerId: ""
        });

        return {
            comp: rootDuplicate,
            hadText: true,
            cleanup: function () {
                for (var i = duplicates.length - 1; i >= 0; i--) {
                    try {
                        duplicates[i].remove();
                    } catch (ignoredRemoveDuplicate) {
                    }
                }
            }
        };
    }

    function compositionHasTextRecursive(comp, seen) {
        var key = compositionKey(comp);
        if (seen[key]) {
            return false;
        }

        seen[key] = true;
        for (var i = 1; i <= comp.numLayers; i++) {
            var layer = comp.layer(i);
            if (layer instanceof TextLayer) {
                return true;
            }

            if (layer instanceof AVLayer && layer.source instanceof CompItem) {
                if (compositionHasTextRecursive(layer.source, seen)) {
                    return true;
                }
            }
        }

        return false;
    }

    function duplicateCompositionGraph(comp, registry, duplicates, warnings) {
        var key = compositionKey(comp);
        if (registry[key]) {
            return registry[key];
        }

        var duplicate = comp.duplicate();
        duplicate.name = comp.name;
        registry[key] = duplicate;
        duplicates.push(duplicate);

        for (var i = 1; i <= duplicate.numLayers; i++) {
            var duplicateLayer = duplicate.layer(i);
            if (!(duplicateLayer instanceof AVLayer) || !(duplicateLayer.source instanceof CompItem)) {
                continue;
            }

            try {
                var duplicateSource = duplicateCompositionGraph(duplicateLayer.source, registry, duplicates, warnings);
                duplicateLayer.replaceSource(duplicateSource, false);
            } catch (error) {
                warnings.push({
                    code: "PRECOMP_DUPLICATE_FAILED",
                    message: "Could not duplicate nested composition for text conversion: " + error.toString(),
                    layerId: "layer-" + duplicateLayer.index
                });
            }
        }

        return duplicate;
    }

    function convertTextLayersToShapesRecursive(comp, seen, warnings) {
        var key = compositionKey(comp);
        if (seen[key]) {
            return;
        }

        seen[key] = true;
        for (var i = 1; i <= comp.numLayers; i++) {
            var layer = comp.layer(i);
            if (layer instanceof AVLayer && layer.source instanceof CompItem) {
                convertTextLayersToShapesRecursive(layer.source, seen, warnings);
            }
        }

        var textLayers = [];
        for (var j = 1; j <= comp.numLayers; j++) {
            var candidate = comp.layer(j);
            if (candidate instanceof TextLayer && candidate.enabled) {
                textLayers.push(candidate);
            }
        }

        if (textLayers.length === 0) {
            return;
        }

        try {
            comp.openInViewer();
        } catch (ignoredOpenViewer) {
        }

        try {
            for (var deselectIndex = 1; deselectIndex <= comp.numLayers; deselectIndex++) {
                comp.layer(deselectIndex).selected = false;
            }

            for (var selectIndex = 0; selectIndex < textLayers.length; selectIndex++) {
                textLayers[selectIndex].selected = true;
            }

            var commandId = app.findMenuCommandId("Create Shapes from Text");
            if (commandId) {
                app.executeCommand(commandId);
            } else {
                throw new Error("After Effects command not found: Create Shapes from Text");
            }

            for (var removeIndex = 0; removeIndex < textLayers.length; removeIndex++) {
                try {
                    textLayers[removeIndex].remove();
                } catch (ignoredRemoveTextLayer) {
                }
            }
        } catch (error) {
            warnings.push({
                code: "TEXT_TO_SHAPES_FAILED",
                message: "Could not convert text to shape paths in temporary comp '" + comp.name + "': " + error.toString(),
                layerId: ""
            });
        }
    }

    function compositionKey(comp) {
        var id = safeValue(function () { return comp.id; }, "");
        return id ? "id-" + id : "name-" + comp.name;
    }

    function collectVectorAnimation(comp, warnings, prepared) {
        var frameRate = Math.max(1, Number(comp.frameRate));
        var duration = Math.max(0, Number(comp.duration));
        var frameCount = Math.max(1, Math.ceil(duration * frameRate - 0.000001));
        var state = {
            map: {},
            primitives: [],
            order: 0,
            warnings: warnings,
            unsupported: false,
            preparedHadText: prepared && prepared.hadText,
            emittedTextPathWarning: false
        };
        var startTime = safeNumber(function () { return comp.displayStartTime; }, 0);

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++) {
            var time = startTime + frameIndex / frameRate;
            var lastValidTime = startTime + Math.max(0, duration - (0.5 / frameRate));
            time = Math.min(time, lastValidTime);
            collectCompVectorFrame(comp, time, frameIndex, matrixIdentity(), 1, "comp:" + sanitizeFileName(comp.name), state, {});
        }

        return {
            enabled: state.primitives.length > 0,
            vectorOnly: state.primitives.length > 0 && !state.unsupported,
            frameCount: frameCount,
            frameRate: frameRate,
            duration: duration,
            primitives: state.primitives
        };
    }

    function collectCompVectorFrame(comp, time, frameIndex, parentMatrix, parentOpacity, pathPrefix, state, compStack) {
        var key = compositionKey(comp);
        if (compStack[key]) {
            markVectorUnsupported(state, "PRECOMP_CYCLE", "Nested composition cycle detected: " + comp.name, "");
            return;
        }

        compStack[key] = true;
        for (var i = comp.numLayers; i >= 1; i--) {
            var layer = comp.layer(i);
            if (!isLayerVisibleAtTime(layer, time)) {
                continue;
            }

            var layerType = getLayerType(layer);
            var layerMatrix = matrixMultiply(parentMatrix, getLayerWorldMatrix(layer, time));
            var layerOpacity = parentOpacity * getLayerOpacity(layer, time);
            var layerPath = pathPrefix + "/layer-" + layer.index + "-" + sanitizeFileName(layer.name);

            if (layerType === "shape") {
                collectShapeLayerVectorFrame(layer, time, frameIndex, layerMatrix, layerOpacity, layerPath, state);
            } else if (layerType === "precomp") {
                var sourceTime = safeNumber(function () { return layer.sourceTime(time); }, time - layer.startTime);
                collectCompVectorFrame(layer.source, sourceTime, frameIndex, layerMatrix, layerOpacity, layerPath, state, compStack);
            } else if (layerType === "footage") {
                markVectorUnsupported(state, "BITMAP_LAYER", "Bitmap or footage layer requires baked fallback: " + layer.name, "layer-" + layer.index);
            } else if (layerType === "text") {
                markVectorUnsupported(state, "TEXT_LAYER_NOT_CONVERTED", "Text layer remained after path conversion and requires baked fallback: " + layer.name, "layer-" + layer.index);
            } else if (layer.enabled) {
                markVectorUnsupported(state, "UNSUPPORTED_LAYER", "Unsupported layer type for vector export: " + layer.name + " (" + layerType + ")", "layer-" + layer.index);
            }
        }

        compStack[key] = false;
    }

    function collectShapeLayerVectorFrame(layer, time, frameIndex, matrix, opacity, pathPrefix, state) {
        var rootVectors = layer.property("ADBE Root Vectors Group");
        if (!rootVectors) {
            return;
        }

        var style = defaultVectorStyle();
        collectVectorGroupFrame(rootVectors, time, frameIndex, matrix, opacity, pathPrefix + "/root", state, style);
    }

    function collectVectorGroupFrame(group, time, frameIndex, matrix, opacity, pathPrefix, state, inheritedStyle) {
        var localStyle = mergeVectorStyle(inheritedStyle, collectDirectVectorStyle(group, time, opacity));

        for (var i = 1; i <= group.numProperties; i++) {
            var property = group.property(i);
            if (!property || property.enabled === false) {
                continue;
            }

            if (property.matchName === "ADBE Vector Group") {
                var vectors = findProperty(property, "ADBE Vectors Group");
                var transform = findProperty(property, "ADBE Vector Transform Group");
                var groupOpacity = opacity * getVectorGroupOpacity(transform, time);
                var groupMatrix = matrixMultiply(matrix, getVectorGroupMatrix(transform, time));
                collectVectorGroupFrame(vectors, time, frameIndex, groupMatrix, groupOpacity, pathPrefix + "/group-" + i + "-" + sanitizeFileName(property.name), state, localStyle);
            } else if (property.matchName === "ADBE Vector Shape - Rect") {
                addRectPrimitiveFrame(property, time, frameIndex, matrix, localStyle, pathPrefix + "/rect-" + i, state);
            } else if (property.matchName === "ADBE Vector Shape - Group") {
                addPathPrimitiveFrame(property, time, frameIndex, matrix, localStyle, pathPrefix + "/path-" + i, state);
            } else if (property.matchName === "ADBE Vector Graphic - Fill" ||
                property.matchName === "ADBE Vector Graphic - Stroke" ||
                property.matchName === "ADBE Vector Transform Group") {
                continue;
            } else if (property.matchName === "ADBE Vector Filter - Trim" ||
                property.matchName === "ADBE Vector Filter - Repeater" ||
                property.matchName === "ADBE Vector Filter - Merge") {
                markVectorUnsupported(state, "UNSUPPORTED_VECTOR_OPERATOR", "Unsupported shape operator requires baked fallback: " + property.name, "");
            }
        }
    }

    function addRectPrimitiveFrame(rectProperty, time, frameIndex, matrix, style, id, state) {
        var size = getVectorAtTime(findProperty(rectProperty, "ADBE Vector Rect Size"), time, [0, 0]);
        var position = getVectorAtTime(findProperty(rectProperty, "ADBE Vector Rect Position"), time, [0, 0]);
        var roundness = getNumberAtTime(findProperty(rectProperty, "ADBE Vector Rect Roundness"), time, 0);
        var width = Number(size[0]);
        var height = Number(size[1]);
        var frame = makeVectorFrame(frameIndex, matrix, style);
        frame.x = Number(position[0]) - width * 0.5;
        frame.y = Number(position[1]) - height * 0.5;
        frame.width = width;
        frame.height = height;
        frame.roundness = roundness;
        addPrimitiveFrame(state, id, rectProperty.name, "rect", frame);
    }

    function addPathPrimitiveFrame(pathProperty, time, frameIndex, matrix, style, id, state) {
        var shapeProperty = findProperty(pathProperty, "ADBE Vector Shape");
        if (!shapeProperty) {
            markVectorUnsupported(state, "MISSING_VECTOR_PATH", "Shape path has no path property: " + pathProperty.name, "");
            return;
        }

        var shape = safeValue(function () { return shapeProperty.valueAtTime(time, false); }, null);
        if (!shape || !shape.vertices || shape.vertices.length < 2) {
            return;
        }

        if (state.preparedHadText && !state.emittedTextPathWarning) {
            state.emittedTextPathWarning = true;
            markVectorUnsupported(state, "TEXT_PATH_BAKED_FALLBACK", "Typography was converted to shape paths, then routed through baked fallback for pixel-accurate glyph holes/curves.", "");
        }

        var frame = makeVectorFrame(frameIndex, matrix, style);
        frame.closed = shape.closed === true;
        frame.path = sampleShapePath(shape, 8);
        addPrimitiveFrame(state, id, pathProperty.name, "path", frame);
    }

    function addPrimitiveFrame(state, id, name, type, frame) {
        var primitive = state.map[id];
        if (!primitive) {
            primitive = {
                id: id,
                name: name,
                type: type,
                order: state.order,
                frames: []
            };
            state.order++;
            state.map[id] = primitive;
            state.primitives.push(primitive);
        }

        primitive.frames.push(frame);
    }

    function makeVectorFrame(frameIndex, matrix, style) {
        return {
            frameIndex: frameIndex,
            visible: (style.fillEnabled || style.strokeEnabled) && style.opacity > 0.0001,
            m00: matrix.m00,
            m01: matrix.m01,
            m02: matrix.m02,
            m10: matrix.m10,
            m11: matrix.m11,
            m12: matrix.m12,
            x: 0,
            y: 0,
            width: 0,
            height: 0,
            roundness: 0,
            fillEnabled: style.fillEnabled,
            fillR: style.fillColor[0],
            fillG: style.fillColor[1],
            fillB: style.fillColor[2],
            fillA: style.fillOpacity * style.opacity,
            strokeEnabled: style.strokeEnabled,
            strokeR: style.strokeColor[0],
            strokeG: style.strokeColor[1],
            strokeB: style.strokeColor[2],
            strokeA: style.strokeOpacity * style.opacity,
            strokeWidth: style.strokeWidth,
            dash: style.dash,
            gap: style.gap,
            dashOffset: style.dashOffset,
            closed: true,
            path: []
        };
    }

    function defaultVectorStyle() {
        return {
            fillEnabled: false,
            fillColor: [1, 1, 1],
            fillOpacity: 1,
            strokeEnabled: false,
            strokeColor: [0, 0, 0],
            strokeOpacity: 1,
            strokeWidth: 1,
            dash: 0,
            gap: 0,
            dashOffset: 0,
            opacity: 1
        };
    }

    function mergeVectorStyle(base, override) {
        var result = defaultVectorStyle();
        copyStyleInto(result, base);
        copyStyleInto(result, override);
        result.opacity = override && override.opacity !== undefined
            ? override.opacity
            : (base && base.opacity !== undefined ? base.opacity : 1);
        return result;
    }

    function copyStyleInto(target, source) {
        if (!source) {
            return;
        }

        for (var key in source) {
            if (source.hasOwnProperty(key) && source[key] !== undefined) {
                target[key] = source[key];
            }
        }
    }

    function collectDirectVectorStyle(group, time, opacity) {
        var style = { opacity: opacity };
        for (var i = 1; i <= group.numProperties; i++) {
            var property = group.property(i);
            if (!property || property.enabled === false) {
                continue;
            }

            if (property.matchName === "ADBE Vector Graphic - Fill") {
                style.fillEnabled = true;
                style.fillColor = normalizeColor(getVectorAtTime(findProperty(property, "ADBE Vector Fill Color"), time, [1, 1, 1]));
                style.fillOpacity = getNumberAtTime(findProperty(property, "ADBE Vector Fill Opacity"), time, 100) / 100;
            } else if (property.matchName === "ADBE Vector Graphic - Stroke") {
                style.strokeEnabled = true;
                style.strokeColor = normalizeColor(getVectorAtTime(findProperty(property, "ADBE Vector Stroke Color"), time, [0, 0, 0]));
                style.strokeOpacity = getNumberAtTime(findProperty(property, "ADBE Vector Stroke Opacity"), time, 100) / 100;
                style.strokeWidth = getNumberAtTime(findProperty(property, "ADBE Vector Stroke Width"), time, 1);
                var dashes = findProperty(property, "ADBE Vector Stroke Dashes");
                style.dash = getNumberAtTime(findProperty(dashes, "ADBE Vector Stroke Dash 1"), time, 0);
                style.gap = getNumberAtTime(findProperty(dashes, "ADBE Vector Stroke Gap 1"), time, 0);
                style.dashOffset = getNumberAtTime(findProperty(dashes, "ADBE Vector Stroke Offset"), time, 0);
            }
        }

        return style;
    }

    function normalizeColor(value) {
        var color = value instanceof Array ? value : [1, 1, 1];
        return [
            clamp01(Number(color.length > 0 ? color[0] : 1)),
            clamp01(Number(color.length > 1 ? color[1] : 1)),
            clamp01(Number(color.length > 2 ? color[2] : 1))
        ];
    }

    function clamp01(value) {
        if (!isFinite(value)) {
            return 0;
        }
        return Math.max(0, Math.min(1, value));
    }

    function getVectorGroupMatrix(transform, time) {
        if (!transform) {
            return matrixIdentity();
        }

        var anchor = getVectorAtTime(findProperty(transform, "ADBE Vector Anchor"), time, [0, 0]);
        var position = getVectorAtTime(findProperty(transform, "ADBE Vector Position"), time, [0, 0]);
        var scale = getVectorAtTime(findProperty(transform, "ADBE Vector Scale"), time, [100, 100]);
        var rotation = getNumberAtTime(findProperty(transform, "ADBE Vector Rotation"), time, 0);
        return makeTransformMatrix(anchor, position, scale, rotation);
    }

    function getVectorGroupOpacity(transform, time) {
        if (!transform) {
            return 1;
        }

        return getNumberAtTime(
            findProperty(transform, "ADBE Vector Group Opacity") || findProperty(transform, "ADBE Vector Opacity"),
            time,
            100) / 100;
    }

    function getLayerWorldMatrix(layer, time) {
        var own = getLayerMatrix(layer, time);
        if (layer.parent) {
            return matrixMultiply(getLayerWorldMatrix(layer.parent, time), own);
        }

        return own;
    }

    function getLayerMatrix(layer, time) {
        var transform = layer.property("ADBE Transform Group");
        var anchor = getVectorAtTime(findProperty(transform, "ADBE Anchor Point"), time, [0, 0, 0]);
        var position = getVectorAtTime(findProperty(transform, "ADBE Position"), time, [0, 0, 0]);
        var scale = getVectorAtTime(findProperty(transform, "ADBE Scale"), time, [100, 100, 100]);
        var rotation = getNumberAtTime(findProperty(transform, "ADBE Rotate Z") || findProperty(transform, "ADBE Rotation"), time, 0);
        return makeTransformMatrix(anchor, position, scale, rotation);
    }

    function getLayerOpacity(layer, time) {
        var transform = layer.property("ADBE Transform Group");
        return getNumberAtTime(findProperty(transform, "ADBE Opacity"), time, 100) / 100;
    }

    function isLayerVisibleAtTime(layer, time) {
        if (!layer || !layer.enabled) {
            return false;
        }

        return time >= layer.inPoint && time < layer.outPoint;
    }

    function getNumberAtTime(property, time, fallback) {
        if (!property) {
            return fallback;
        }

        var value = safeValue(function () { return property.valueAtTime(time, false); }, fallback);
        if (value instanceof Array) {
            value = value.length > 0 ? value[0] : fallback;
        }

        value = Number(value);
        return isFinite(value) ? value : fallback;
    }

    function getVectorAtTime(property, time, fallback) {
        var value = property ? safeValue(function () { return property.valueAtTime(time, false); }, fallback) : fallback;
        if (!(value instanceof Array)) {
            value = fallback;
        }

        var result = [];
        for (var i = 0; i < fallback.length; i++) {
            var numberValue = Number(value.length > i ? value[i] : fallback[i]);
            result.push(isFinite(numberValue) ? numberValue : fallback[i]);
        }

        return result;
    }

    function makeTransformMatrix(anchor, position, scale, rotation) {
        var sx = Number(scale.length > 0 ? scale[0] : 100) / 100;
        var sy = Number(scale.length > 1 ? scale[1] : sx * 100) / 100;
        var px = Number(position.length > 0 ? position[0] : 0);
        var py = Number(position.length > 1 ? position[1] : 0);
        var ax = Number(anchor.length > 0 ? anchor[0] : 0);
        var ay = Number(anchor.length > 1 ? anchor[1] : 0);
        return matrixMultiply(
            matrixTranslate(px, py),
            matrixMultiply(
                matrixRotate(rotation),
                matrixMultiply(matrixScale(sx, sy), matrixTranslate(-ax, -ay))));
    }

    function matrixIdentity() {
        return { m00: 1, m01: 0, m02: 0, m10: 0, m11: 1, m12: 0 };
    }

    function matrixTranslate(x, y) {
        return { m00: 1, m01: 0, m02: x, m10: 0, m11: 1, m12: y };
    }

    function matrixScale(x, y) {
        return { m00: x, m01: 0, m02: 0, m10: 0, m11: y, m12: 0 };
    }

    function matrixRotate(degrees) {
        var radians = Number(degrees) * Math.PI / 180;
        var c = Math.cos(radians);
        var s = Math.sin(radians);
        return { m00: c, m01: -s, m02: 0, m10: s, m11: c, m12: 0 };
    }

    function matrixMultiply(a, b) {
        return {
            m00: a.m00 * b.m00 + a.m01 * b.m10,
            m01: a.m00 * b.m01 + a.m01 * b.m11,
            m02: a.m00 * b.m02 + a.m01 * b.m12 + a.m02,
            m10: a.m10 * b.m00 + a.m11 * b.m10,
            m11: a.m10 * b.m01 + a.m11 * b.m11,
            m12: a.m10 * b.m02 + a.m11 * b.m12 + a.m12
        };
    }

    function sampleShapePath(shape, segmentsPerCurve) {
        var result = [];
        var vertices = shape.vertices;
        var inTangents = shape.inTangents || [];
        var outTangents = shape.outTangents || [];
        var count = vertices.length;
        var lastSegment = shape.closed ? count : count - 1;

        for (var i = 0; i < lastSegment; i++) {
            var next = (i + 1) % count;
            var p0 = vertices[i];
            var p3 = vertices[next];
            var outTangent = outTangents[i] || [0, 0];
            var inTangent = inTangents[next] || [0, 0];
            var p1 = [p0[0] + outTangent[0], p0[1] + outTangent[1]];
            var p2 = [p3[0] + inTangent[0], p3[1] + inTangent[1]];
            var curved = Math.abs(outTangent[0]) + Math.abs(outTangent[1]) + Math.abs(inTangent[0]) + Math.abs(inTangent[1]) > 0.001;
            var segments = curved ? segmentsPerCurve : 1;
            for (var step = 0; step < segments; step++) {
                var t = step / segments;
                var point = cubicPoint(p0, p1, p2, p3, t);
                result.push({ x: point[0], y: point[1], inX: 0, inY: 0, outX: 0, outY: 0 });
            }
        }

        if (!shape.closed && count > 0) {
            var last = vertices[count - 1];
            result.push({ x: last[0], y: last[1], inX: 0, inY: 0, outX: 0, outY: 0 });
        }

        return result;
    }

    function cubicPoint(p0, p1, p2, p3, t) {
        var oneMinusT = 1 - t;
        var a = oneMinusT * oneMinusT * oneMinusT;
        var b = 3 * oneMinusT * oneMinusT * t;
        var c = 3 * oneMinusT * t * t;
        var d = t * t * t;
        return [
            a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0],
            a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]
        ];
    }

    function markVectorUnsupported(state, code, message, layerId) {
        state.unsupported = true;
        state.warnings.push({
            code: code,
            message: message,
            layerId: layerId || ""
        });
    }

    function writeTextFile(file, text) {
        file.encoding = "UTF-8";
        if (!file.open("w")) {
            throw new Error("Could not write file: " + file.fsName);
        }

        file.write(text);
        file.close();
    }

    function isUnityProjectFolder(folder) {
        if (!folder || !folder.exists) {
            return false;
        }

        return new Folder(folder.fsName + "/Assets").exists &&
            new Folder(folder.fsName + "/ProjectSettings").exists;
    }

    function ensureFolderExists(folder) {
        if (folder.exists) {
            return;
        }

        var parent = folder.parent;
        if (parent && !parent.exists) {
            ensureFolderExists(parent);
        }

        if (!folder.exists && !folder.create()) {
            throw new Error("Could not create folder: " + folder.fsName);
        }
    }

    function joinPath(root, relativePath) {
        return String(root).replace(/[\\\/]+$/, "") + "/" + normalizeRelativePath(relativePath);
    }

    function normalizeRelativePath(path) {
        var normalized = String(path || DEFAULT_UNITY_EXPORT_RELATIVE_PATH).replace(/\\/g, "/");
        normalized = normalized.replace(/^\/+/, "").replace(/\/+$/, "");
        return normalized || DEFAULT_UNITY_EXPORT_RELATIVE_PATH;
    }

    function normalizeExtension(extension) {
        var normalized = String(extension || "mp4").replace(/^\./, "").toLowerCase();
        if (!normalized) {
            normalized = "mp4";
        }

        return normalized;
    }

    function loadSetting(key, fallback) {
        try {
            if (app.settings.haveSetting(SETTINGS_SECTION, key)) {
                return app.settings.getSetting(SETTINGS_SECTION, key);
            }
        } catch (ignoredSettingLoad) {
        }

        for (var i = 0; i < LEGACY_SETTINGS_SECTIONS.length; i++) {
            try {
                if (app.settings.haveSetting(LEGACY_SETTINGS_SECTIONS[i], key)) {
                    return app.settings.getSetting(LEGACY_SETTINGS_SECTIONS[i], key);
                }
            } catch (ignoredLegacySettingLoad) {
            }
        }

        return fallback;
    }

    function saveSetting(key, value) {
        try {
            app.settings.saveSetting(SETTINGS_SECTION, key, String(value));
        } catch (ignoredSettingSave) {
        }
    }

    function collectComp(comp) {
        var colorSpace = "unknown";
        var bitDepth = 8;

        try {
            colorSpace = app.project.workingSpace || "none";
        } catch (ignoredColorSpace) {
            colorSpace = "unknown";
        }

        try {
            bitDepth = app.project.bitsPerChannel || 8;
        } catch (ignoredBitDepth) {
            bitDepth = 8;
        }

        return {
            name: comp.name,
            width: comp.width,
            height: comp.height,
            frameRate: comp.frameRate,
            duration: comp.duration,
            workAreaStart: comp.workAreaStart,
            workAreaDuration: comp.workAreaDuration,
            colorSpace: colorSpace,
            bitDepth: bitDepth
        };
    }

    function collectLayer(layer, assetMap, assets, warnings) {
        var sourceAssetId = collectAsset(layer, assetMap, assets);

        return {
            id: "layer-" + layer.index,
            index: layer.index,
            name: layer.name,
            type: getLayerType(layer),
            enabled: layer.enabled,
            threeDLayer: safeBoolean(function () { return layer.threeDLayer; }, false),
            startTime: layer.startTime,
            inPoint: layer.inPoint,
            outPoint: layer.outPoint,
            parentId: layer.parent ? "layer-" + layer.parent.index : "",
            blendMode: enumName(safeValue(function () { return layer.blendingMode; }, "NORMAL")),
            matteMode: enumName(safeValue(function () { return layer.trackMatteType; }, "NO_TRACK_MATTE")),
            sourceAssetId: sourceAssetId,
            transform: collectTransform(layer),
            effects: collectEffects(layer, warnings),
            masks: collectMasks(layer),
            expressions: collectLayerExpressions(layer)
        };
    }

    function collectAsset(layer, assetMap, assets) {
        if (!(layer instanceof AVLayer) || !layer.source) {
            return "";
        }

        var source = layer.source;
        var sourceId = safeValue(function () { return source.id; }, "");
        var id = "asset-" + (sourceId ? sourceId : sanitizeFileName(source.name));
        if (assetMap[id]) {
            return id;
        }

        var path = "";
        try {
            if (source.file) {
                path = source.file.fsName;
            }
        } catch (ignoredFilePath) {
            path = "";
        }

        var type = "unknown";
        if (source instanceof CompItem) {
            type = "precomp";
        } else if (source instanceof FootageItem) {
            type = "footage";
        }

        assetMap[id] = true;
        assets.push({
            id: id,
            name: source.name,
            type: type,
            path: path,
            width: safeNumber(function () { return source.width; }, 0),
            height: safeNumber(function () { return source.height; }, 0),
            frameRate: safeNumber(function () { return source.frameRate; }, 0),
            duration: safeNumber(function () { return source.duration; }, 0)
        });

        return id;
    }

    function collectTransform(layer) {
        var transform = layer.property("ADBE Transform Group");
        return {
            anchorPoint: collectAnimatedVector3(findProperty(transform, "ADBE Anchor Point"), [0, 0, 0]),
            position: collectAnimatedVector3(findProperty(transform, "ADBE Position"), [0, 0, 0]),
            scale: collectAnimatedVector3(findProperty(transform, "ADBE Scale"), [100, 100, 100]),
            rotation: collectAnimatedFloat(findProperty(transform, "ADBE Rotate Z") || findProperty(transform, "ADBE Rotation"), 0),
            opacity: collectAnimatedFloat(findProperty(transform, "ADBE Opacity"), 100)
        };
    }

    function collectAnimatedFloat(property, fallback) {
        var result = {
            value: fallback,
            expression: "",
            keys: []
        };

        if (!property) {
            return result;
        }

        var value = safeValue(function () { return property.value; }, fallback);
        if (value instanceof Array) {
            value = value.length > 0 ? value[0] : fallback;
        }
        result.value = Number(value);
        result.expression = collectExpression(property);

        for (var i = 1; i <= property.numKeys; i++) {
            var keyValue = property.keyValue(i);
            if (keyValue instanceof Array) {
                keyValue = keyValue.length > 0 ? keyValue[0] : fallback;
            }

            result.keys.push({
                time: property.keyTime(i),
                value: Number(keyValue)
            });
        }

        return result;
    }

    function collectAnimatedVector3(property, fallback) {
        var value = property ? safeValue(function () { return property.value; }, fallback) : fallback;
        var result = vector3Object(value, fallback);
        result.expression = property ? collectExpression(property) : "";
        result.keys = [];

        if (!property) {
            return result;
        }

        for (var i = 1; i <= property.numKeys; i++) {
            var keyVector = vector3Object(property.keyValue(i), fallback);
            keyVector.time = property.keyTime(i);
            result.keys.push(keyVector);
        }

        return result;
    }

    function collectExpression(property) {
        try {
            if (property.canSetExpression && property.expressionEnabled && property.expression) {
                return property.expression;
            }
        } catch (ignoredExpression) {
            return "";
        }

        return "";
    }

    function collectLayerExpressions(layer) {
        var expressions = [];
        collectExpressionsRecursive(layer, expressions);
        return expressions;
    }

    function collectExpressionsRecursive(group, expressions) {
        if (!group || !group.numProperties) {
            return;
        }

        for (var i = 1; i <= group.numProperties; i++) {
            var property = group.property(i);
            if (!property) {
                continue;
            }

            var expression = collectExpression(property);
            if (expression) {
                expressions.push(property.matchName + ": " + expression);
            }

            collectExpressionsRecursive(property, expressions);
        }
    }

    function collectEffects(layer, warnings) {
        var effects = [];
        var parade = layer.property("ADBE Effect Parade");
        if (!parade) {
            return effects;
        }

        for (var i = 1; i <= parade.numProperties; i++) {
            var effect = parade.property(i);
            var parameters = [];

            for (var p = 1; p <= effect.numProperties; p++) {
                var param = effect.property(p);
                parameters.push({
                    name: param.name,
                    matchName: param.matchName,
                    valueType: String(param.propertyValueType),
                    value: stringifyPropertyValue(safeValue(function () { return param.value; }, ""))
                });
            }

            effects.push({
                matchName: effect.matchName,
                displayName: effect.name,
                enabled: safeBoolean(function () { return effect.enabled; }, true),
                parameters: parameters
            });

            if (effect.matchName.indexOf("ADBE") !== 0) {
                warnings.push({
                    code: "THIRD_PARTY_EFFECT",
                    message: "Third-party effect should be baked or implemented manually: " + effect.name,
                    layerId: "layer-" + layer.index
                });
            }
        }

        return effects;
    }

    function collectMasks(layer) {
        var masks = [];
        var parade = layer.property("ADBE Mask Parade");
        if (!parade) {
            return masks;
        }

        for (var i = 1; i <= parade.numProperties; i++) {
            var mask = parade.property(i);
            var feather = safeValue(function () {
                return mask.property("ADBE Mask Feather").value;
            }, [0, 0]);

            masks.push({
                name: mask.name,
                mode: enumName(safeValue(function () { return mask.maskMode; }, "ADD")),
                inverted: safeBoolean(function () { return mask.inverted; }, false),
                opacity: safeNumber(function () { return mask.property("ADBE Mask Opacity").value; }, 100),
                expansion: safeNumber(function () { return mask.property("ADBE Mask Expansion").value; }, 0),
                featherX: feather instanceof Array ? Number(feather[0]) : Number(feather),
                featherY: feather instanceof Array && feather.length > 1 ? Number(feather[1]) : 0
            });
        }

        return masks;
    }

    function saveReferenceFrames(comp, folder, warnings) {
        var times = [
            comp.workAreaStart,
            comp.workAreaStart + comp.workAreaDuration * 0.5,
            Math.max(comp.workAreaStart, comp.workAreaStart + comp.workAreaDuration - (1 / comp.frameRate))
        ];

        for (var i = 0; i < times.length; i++) {
            var frameFile = new File(folder.fsName + "/" + sanitizeFileName(comp.name) + "_ref_" + i + ".png");
            try {
                comp.saveFrameToPng(times[i], frameFile);
                warnIfPngMissingAlpha(frameFile, warnings, "REFERENCE_FRAME_ALPHA_MISSING", "Reference frame does not contain an alpha channel: " + frameFile.name);
            } catch (error) {
                warnings.push({
                    code: "REFERENCE_FRAME_FAILED",
                    message: "Could not export reference frame at " + times[i] + "s: " + error.toString(),
                    layerId: ""
                });
            }
        }
    }

    function saveBakedFrames(comp, exportFolder, warnings) {
        var safeCompName = sanitizeFileName(comp.name);
        var relativePath = safeCompName + ".frames";
        var framesFolder = new Folder(exportFolder.fsName + "/" + relativePath);
        ensureFolderExists(framesFolder);

        var oldFrames = framesFolder.getFiles("*.png");
        for (var oldIndex = 0; oldIndex < oldFrames.length; oldIndex++) {
            try {
                if (oldFrames[oldIndex] instanceof File) {
                    oldFrames[oldIndex].remove();
                }
            } catch (ignoredRemoveFrame) {
            }
        }

        var frameRate = Math.max(1, Number(comp.frameRate));
        var duration = Math.max(0, Number(comp.duration));
        var frameCount = Math.max(1, Math.ceil(duration * frameRate - 0.000001));
        var startTime = safeNumber(function () { return comp.displayStartTime; }, 0);
        var filePrefix = safeCompName + "_frame_";
        var writtenCount = 0;
        var alphaOk = true;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++) {
            var time = startTime + frameIndex / frameRate;
            var lastValidTime = startTime + Math.max(0, duration - (0.5 / frameRate));
            time = Math.min(time, lastValidTime);
            var frameFile = new File(framesFolder.fsName + "/" + filePrefix + zeroPad(frameIndex, 5) + ".png");
            try {
                comp.saveFrameToPng(time, frameFile);
                if (!pngHasAlphaChannelWithRetry(frameFile)) {
                    alphaOk = false;
                    warnings.push({
                        code: "BAKED_FRAME_ALPHA_MISSING",
                        message: "Baked PNG frame does not contain an alpha channel: " + frameFile.name,
                        layerId: ""
                    });
                }
                writtenCount++;
            } catch (error) {
                warnings.push({
                    code: "BAKED_FRAME_FAILED",
                    message: "Could not bake animation frame " + frameIndex + " at " + time + "s: " + error.toString(),
                    layerId: ""
                });
                break;
            }
        }

        return {
            enabled: writtenCount === frameCount,
            relativePath: relativePath,
            filePrefix: filePrefix,
            fileExtension: "png",
            frameCount: writtenCount,
            width: comp.width,
            height: comp.height,
            frameRate: frameRate,
            duration: writtenCount / frameRate,
            startTime: startTime,
            hasAlpha: alphaOk
        };
    }

    function warnIfPngMissingAlpha(file, warnings, code, message) {
        if (!pngHasAlphaChannelWithRetry(file)) {
            warnings.push({
                code: code,
                message: message,
                layerId: ""
            });
        }
    }

    function pngHasAlphaChannelWithRetry(file) {
        for (var attempt = 0; attempt < 5; attempt++) {
            if (pngHasAlphaChannel(file)) {
                return true;
            }

            sleepMilliseconds(80);
        }

        return false;
    }

    function pngHasAlphaChannel(file) {
        if (!file || !file.exists) {
            return false;
        }

        try {
            file.encoding = "BINARY";
            if (!file.open("r")) {
                return false;
            }

            var bytes = file.read(26);
            file.close();
            if (!bytes || bytes.length < 26) {
                return false;
            }

            var signature = [137, 80, 78, 71, 13, 10, 26, 10];
            for (var i = 0; i < signature.length; i++) {
                if (bytes.charCodeAt(i) !== signature[i]) {
                    return false;
                }
            }

            var colorType = bytes.charCodeAt(25);
            return colorType === 4 || colorType === 6;
        } catch (ignoredAlphaCheck) {
            try {
                file.close();
            } catch (ignoredClose) {
            }
            return false;
        }
    }

    function sleepMilliseconds(milliseconds) {
        var end = new Date().getTime() + milliseconds;
        while (new Date().getTime() < end) {
        }
    }

    function findProperty(group, matchName) {
        if (!group) {
            return null;
        }

        for (var i = 1; i <= group.numProperties; i++) {
            var property = group.property(i);
            if (property && property.matchName === matchName) {
                return property;
            }
        }

        return null;
    }

    function getLayerType(layer) {
        if (layer instanceof CameraLayer) {
            return "camera";
        }
        if (layer instanceof LightLayer) {
            return "light";
        }
        if (layer instanceof TextLayer) {
            return "text";
        }
        if (layer instanceof ShapeLayer) {
            return "shape";
        }
        if (layer instanceof AVLayer && layer.source instanceof CompItem) {
            return "precomp";
        }
        if (layer instanceof AVLayer && layer.source instanceof FootageItem) {
            return "footage";
        }

        return "unknown";
    }

    function vector3Object(value, fallback) {
        var source = value instanceof Array ? value : fallback;
        return {
            x: Number(source.length > 0 ? source[0] : fallback[0]),
            y: Number(source.length > 1 ? source[1] : fallback[1]),
            z: Number(source.length > 2 ? source[2] : fallback[2])
        };
    }

    function enumName(value) {
        var text = String(value);
        var dot = text.lastIndexOf(".");
        if (dot >= 0) {
            text = text.substring(dot + 1);
        }
        return text;
    }

    function stringifyPropertyValue(value) {
        if (value instanceof Array) {
            var parts = [];
            for (var i = 0; i < value.length; i++) {
                parts.push(String(value[i]));
            }
            return parts.join(",");
        }

        return String(value);
    }

    function sanitizeFileName(value) {
        return String(value).replace(/[\\\/\:\*\?\"\<\>\|]/g, "_");
    }

    function zeroPad(value, width) {
        var text = String(Math.max(0, Math.floor(Number(value))));
        while (text.length < width) {
            text = "0" + text;
        }
        return text;
    }

    function createJobId(comp) {
        var stamp = new Date().getTime();
        var random = Math.floor(Math.random() * 1000000);
        return "ae_" + sanitizeFileName(comp.name).replace(/\s+/g, "_") + "_" + stamp + "_" + random;
    }

    function compactResultText(text) {
        var status = matchJsonString(text, "status") || "Unknown";
        var message = matchJsonString(text, "message") || "";
        var prefab = matchJsonString(text, "generatedPrefabPath") || "";
        if (prefab) {
            return status + ": " + message + " -> " + prefab;
        }

        var material = matchJsonString(text, "generatedMaterialPath") || "";
        if (material) {
            return status + ": " + message + " -> " + material;
        }

        return status + ": " + message;
    }

    function matchJsonString(text, key) {
        var pattern = new RegExp("\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
        var match = pattern.exec(text);
        return match ? match[1].replace(/\\n/g, "\n").replace(/\\"/g, "\"") : "";
    }

    function safeValue(callback, fallback) {
        try {
            return callback();
        } catch (ignored) {
            return fallback;
        }
    }

    function safeNumber(callback, fallback) {
        var value = safeValue(callback, fallback);
        value = Number(value);
        return isFinite(value) ? value : fallback;
    }

    function safeBoolean(callback, fallback) {
        var value = safeValue(callback, fallback);
        if (value === true || value === false) {
            return value;
        }
        if (value === "true") {
            return true;
        }
        if (value === "false") {
            return false;
        }
        return fallback;
    }

    function toJson(value) {
        if (value === null || value === undefined) {
            return "null";
        }

        var type = typeof value;
        if (type === "number") {
            return isFinite(value) ? String(value) : "0";
        }
        if (type === "boolean") {
            return value ? "true" : "false";
        }
        if (type === "string") {
            return quoteJson(value);
        }
        if (value instanceof Array) {
            var arrayParts = [];
            for (var i = 0; i < value.length; i++) {
                arrayParts.push(toJson(value[i]));
            }
            return "[" + arrayParts.join(",") + "]";
        }

        var objectParts = [];
        for (var key in value) {
            if (value.hasOwnProperty(key) && typeof value[key] !== "function") {
                objectParts.push(quoteJson(key) + ":" + toJson(value[key]));
            }
        }
        return "{" + objectParts.join(",") + "}";
    }

    function quoteJson(value) {
        return "\"" + String(value)
            .replace(/\\/g, "\\\\")
            .replace(/\"/g, "\\\"")
            .replace(/\r/g, "\\r")
            .replace(/\n/g, "\\n")
            .replace(/\t/g, "\\t") + "\"";
    }

    function findCompositionByName(name) {
        if (!app.project) {
            return null;
        }

        for (var i = 1; i <= app.project.numItems; i++) {
            var item = app.project.item(i);
            if (item instanceof CompItem && item.name === name) {
                return item;
            }
        }

        return null;
    }

    function listCompositionNames() {
        var names = [];
        if (!app.project) {
            return names;
        }

        for (var i = 1; i <= app.project.numItems; i++) {
            var item = app.project.item(i);
            if (item instanceof CompItem) {
                names.push(item.name);
            }
        }
        return names;
    }

    function inspectPropertyTree(group, depth) {
        var properties = [];
        if (!group || !group.numProperties || depth > 12) {
            return properties;
        }

        for (var i = 1; i <= group.numProperties; i++) {
            var property = group.property(i);
            if (!property) {
                continue;
            }

            var entry = {
                name: property.name,
                matchName: property.matchName,
                propertyType: String(property.propertyType),
                valueType: String(property.propertyValueType),
                numKeys: property.numKeys || 0,
                children: []
            };
            if (property.numProperties) {
                entry.children = inspectPropertyTree(property, depth + 1);
            }
            properties.push(entry);
        }
        return properties;
    }

    function inspectCompositionRecursive(comp, visited) {
        var key = "comp-" + comp.id;
        if (visited[key]) {
            return null;
        }
        visited[key] = true;

        var result = {
            id: key,
            name: comp.name,
            width: comp.width,
            height: comp.height,
            duration: comp.duration,
            frameRate: comp.frameRate,
            layers: [],
            nested: []
        };

        for (var i = 1; i <= comp.numLayers; i++) {
            var layer = comp.layer(i);
            var layerInfo = {
                index: layer.index,
                name: layer.name,
                type: getLayerType(layer),
                threeDLayer: safeBoolean(function () { return layer.threeDLayer; }, false),
                sourceName: "",
                properties: []
            };
            var rootVectors = layer.property("ADBE Root Vectors Group");
            if (rootVectors) {
                layerInfo.properties = inspectPropertyTree(rootVectors, 0);
            }

            if (layer instanceof AVLayer && layer.source instanceof CompItem) {
                layerInfo.sourceName = layer.source.name;
                var nested = inspectCompositionRecursive(layer.source, visited);
                if (nested) {
                    result.nested.push(nested);
                }
            }
            result.layers.push(layerInfo);
        }
        return result;
    }

    $.global.AE2UnityAutomation = {
        listCompositions: function () {
            return listCompositionNames().join("\n");
        },
        inspectComposition: function (compName) {
            var comp = findCompositionByName(compName);
            if (!comp) {
                throw new Error("Composition not found: " + compName);
            }
            return toJson(inspectCompositionRecursive(comp, {}));
        },
        exportCompositionToUnity: function (compName, projectPath, relativePath) {
            var comp = findCompositionByName(compName);
            if (!comp) {
                throw new Error("Composition not found: " + compName);
            }

            var outputFile = exportActiveCompToUnityProject(projectPath, relativePath, {
                comp: comp,
                exportReferenceFrames: true,
                exportBakedFrames: true
            });
            return outputFile.fsName;
        },
        exportCompositionToBridge: function (compName, projectPath, relativePath) {
            var comp = findCompositionByName(compName);
            if (!comp) {
                throw new Error("Composition not found: " + compName);
            }

            var result = sendActiveCompToUnityBridge(projectPath, relativePath, {
                comp: comp,
                exportReferenceFrames: true,
                exportBakedFrames: true,
                generateShaderAndMaterial: true
            });
            return result.jobId;
        }
    };

    var panel = buildPanel(thisObj);
    if (panel instanceof Window) {
        panel.center();
        panel.show();
    }
})(this);
