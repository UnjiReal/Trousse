"use strict";

const fs = require("fs");
const path = require("path");
const { actions, selectors, util, log } = require("vortex-api");

const gameId = "skyrimse";
const toolId = "trousse-outfit-composer";
const executable = path.join(__dirname, "Trousse.exe");

function publishIcon() {
    try {
        const folder = path.join(util.getVortexPath("userData"), gameId, "icons");
        fs.mkdirSync(folder, { recursive: true });
        fs.copyFileSync(path.join(__dirname, "trousse-tool.png"), path.join(folder, toolId + ".png"), fs.constants.COPYFILE_EXCL);
    } catch (error) {
        if (error.code !== "EEXIST") log("debug", "Trousse tool icon could not be installed", { code: error.code });
    }
}

function registerShortcut(api) {
    const state = api.getState();
    const game = selectors.discoveryByGame(state, gameId);
    if (!game?.path || !fs.existsSync(executable)) return;

    const tools = game.tools ?? {};
    const existing = tools[toolId];
    if (!existing && Object.values(tools).some(tool =>
        !tool.hidden && tool.path && path.basename(tool.path).toLowerCase() === "trousse.exe" &&
        fs.existsSync(tool.path))) return;

    const staging = selectors.installPathForGame(state, gameId) ?? "";
    if (existing?.path !== executable || !existing.custom ||
        existing.environment?.TROUSSE_VORTEX_STAGING !== staging ||
        existing.environment?.TROUSSE_SKYRIM_PATH !== game.path) {
        publishIcon();
        api.store.dispatch(actions.addDiscoveredTool(gameId, toolId, {
            id: toolId,
            name: "Trousse",
            requiredFiles: [],
            executable: null,
            parameters: [],
            ...existing,
            custom: true,
            path: executable,
            workingDirectory: __dirname,
            environment: {
                ...existing?.environment,
                TROUSSE_VORTEX_STAGING: staging,
                TROUSSE_SKYRIM_PATH: game.path,
            },
        }, true));
    }
    // The discovery reducer keeps the previous visibility when the tool is updated.
    if (existing?.hidden) api.store.dispatch(actions.setToolVisible(gameId, toolId, true));
}

function init(context) {
    context.once(() => {
        const { api } = context;
        const refresh = () => registerShortcut(api);
        api.events.on("gamemode-activated", id => {
            if (id === gameId) refresh();
        });
        api.onStateChange(["settings", "mods", "installPath"], refresh);
        api.onStateChange(["settings", "gameMode", "discovered", gameId, "path"], refresh);
        refresh();
    });
    return true;
}

module.exports = { default: init };
