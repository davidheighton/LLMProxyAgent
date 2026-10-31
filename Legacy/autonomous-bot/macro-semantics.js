"use strict";

const STATIC_KEY_SEMANTICS = {
  "*": { label: "submit current line / finish input", category: "control", safety: "context-dependent", effect: "submits the current prompt" },
  "?": { label: "show help for the current menu", category: "navigation", safety: "safe", effect: "prints the current menu help" },
  "Q": { label: "quit or back out of the current menu", category: "navigation", safety: "context-dependent", effect: "returns to the parent menu or command prompt" },
  "P": { label: "open the port or StarDock menu", category: "navigation", safety: "context-dependent", effect: "enters port-related navigation" },
  "S": { label: "enter StarDock / select a StarDock option", category: "navigation", safety: "context-dependent", effect: "enters or selects StarDock" },
  "C": { label: "open the computer menu", category: "navigation", safety: "safe", effect: "enters Computer commands" },
  "N": { label: "open the computer settings menu", category: "configuration", safety: "safe", effect: "enters CN settings" },
  "V": { label: "view or display information", category: "information", safety: "safe", effect: "prints a game information view" },
  "I": { label: "request information or status", category: "information", safety: "safe", effect: "prints current game status" },
  "D": { label: "display or dump data", category: "information", safety: "context-dependent", effect: "prints a data report selected by the current menu" },
  "M": { label: "move / select a movement command", category: "navigation", safety: "context-dependent", effect: "changes movement state or selects movement" },
  "B": { label: "buy or select a buy/settings option", category: "commerce", safety: "context-dependent", effect: "opens a purchase or settings branch" },
  "A": { label: "attack or select option A", category: "combat", safety: "dangerous", effect: "may initiate combat; menu context can make it a non-combat selection" },
  "R": { label: "refurbish, radio, or select option R", category: "operations", safety: "context-dependent", effect: "meaning is determined by the active menu" },
  "Y": { label: "confirm yes", category: "confirmation", safety: "dangerous", effect: "accepts the current confirmation prompt" },
  "O": { label: "select option O", category: "menu", safety: "context-dependent", effect: "selects the O option in the active menu" },
  "T": { label: "select option T or tow/transport", category: "operations", safety: "context-dependent", effect: "meaning is determined by the active menu" },
};

const MAIN_MENU_SEMANTICS = {
  "A": { label: "attack enemy ship", category: "combat", safety: "dangerous", effect: "opens the attack flow" },
  "C": { label: "open Onboard Computer", category: "navigation", safety: "safe", effect: "enters Computer commands" },
  "D": { label: "re-display sector", category: "information", safety: "safe", effect: "prints the current sector display" },
  "I": { label: "show ship information", category: "information", safety: "safe", effect: "prints ship information" },
  "M": { label: "move to a sector", category: "navigation", safety: "context-dependent", effect: "opens the movement prompt" },
  "P": { label: "open port or StarDock", category: "navigation", safety: "context-dependent", effect: "opens port selection" },
  "Q": { label: "quit and exit", category: "navigation", safety: "safe", effect: "leaves the current command flow" },
  "V": { label: "view game status", category: "information", safety: "safe", effect: "prints game status" },
};

const COMPOUND_COMMANDS = {
  cby: {
    label: "Computer menu self-destruct command",
    category: "destructive",
    safety: "dangerous",
    effect: "opens Computer, selects self-destruct, then requires Y at ARE YOU SURE CAPTAIN? (Y/N)",
    certainty: "command-confirmed",
    confirmation: "Y/N",
  },
  jy: {
    label: "jettison cargo command",
    category: "cargo",
    safety: "dangerous",
    effect: "opens the jettison-cargo action and may require a quantity or confirmation",
    certainty: "command-confirmed",
  },
};

function normalizeMenu(menu) {
  const value = String(menu || "unknown").trim().toLowerCase();
  return value === "stardock" ? "stardock-root" : value || "unknown";
}

function tokenizeMacro(macro) {
  return [...String(macro == null ? "" : macro)].filter((token) => !/\s/.test(token));
}

function describeToken(token, context = {}) {
  const key = String(token || "");
  const upper = key.toUpperCase();
  const variable = /^\$|^#/.test(key);
  const numeric = /^\d+$/.test(key);
  if (variable) {
    return { key, label: `dynamic value ${key}`, category: "input", safety: "unknown", effect: "value is resolved at runtime", certainty: "dynamic" };
  }
  if (numeric) {
    return { key, label: `numeric input ${key}`, category: "input", safety: "context-dependent", effect: "answers the current numeric prompt", certainty: "context-dependent" };
  }
  if (normalizeMenu(context.menu) === "main" && MAIN_MENU_SEMANTICS[upper]) {
    return { key, ...MAIN_MENU_SEMANTICS[upper], certainty: "menu-confirmed" };
  }
  if (STATIC_KEY_SEMANTICS[upper]) {
    return { key, ...STATIC_KEY_SEMANTICS[upper], certainty: "generic" };
  }
  if (/^[A-Za-z?+]$/.test(key)) {
    return { key, label: `menu key ${key}`, category: "menu", safety: "unknown", effect: `selects ${key} in the active menu`, certainty: "menu-dependent" };
  }
  return { key, label: "unparsed command fragment", category: "unknown", safety: "blocked", effect: "cannot determine the server interpretation", certainty: "unknown" };
}

function describeMacro(macro, options = {}) {
  const normalized = String(macro == null ? "" : macro).replace(/\s+/g, "").toLowerCase();
  const compound = COMPOUND_COMMANDS[normalized] || null;
  const tokens = compound ? [normalized] : tokenizeMacro(macro);
  const startMenu = normalizeMenu(options.startMenu);
  const steps = tokens.map((token, index) => ({
    index: index + 1,
    menu: startMenu,
    ...(compound ? { key: token, ...compound } : describeToken(token, { menu: startMenu })),
  }));
  const hasUnknown = steps.some((step) => ["unknown", "blocked"].includes(step.certainty) || step.safety === "blocked");
  const hasDanger = steps.some((step) => ["dangerous", "unknown"].includes(step.safety));
  return {
    macro: String(macro == null ? "" : macro),
    startMenu,
    endMenu: startMenu,
    tokens,
    steps,
    exact: !hasUnknown && steps.every((step) => !["menu-dependent", "generic"].includes(step.certainty)),
    allowed: tokens.length > 0 && !hasUnknown,
    risk: hasDanger ? "review" : "normal",
    summary: steps.map((step) => `${step.key}: ${step.label}`).join("; "),
  };
}

function getStaticTokenSemantics(key) {
  return STATIC_KEY_SEMANTICS[String(key || "").toUpperCase()] || null;
}

module.exports = { describeMacro, describeToken, getStaticTokenSemantics, normalizeMenu, tokenizeMacro };