/**
 * @file Entry point that wires together state, UI and animation modules.
 */

import {
  loadStateFromCookie,
  queueSaveStateToCookie,
  applyPreset,
  getActiveEntries
} from './state.js';
import { createWheel } from './wheel.js';
import { WheelAnimationController } from './animation.js';
import { CharacterListUI } from './ui.js';

/**
 * @typedef {Event & {
 *   prompt: () => Promise<void>,
 *   userChoice: Promise<{ outcome: 'accepted' | 'dismissed', platform: string }>
 * }} BeforeInstallPromptEvent
 */

/**
 * DOMクエリの簡易ヘルパー。
 * @template {Element} T
 * @param {string} selector CSSセレクタ。
 * @param {ParentNode} [parent=document] 親要素。
 * @returns {T}
 */
function $(selector, parent = document) {
  const el = parent.querySelector(selector);
  if (!el) {
    throw new Error(`Element not found for selector: ${selector}`);
  }
  return /** @type {T} */ (el);
}

/** 初期化処理 */
function init() {
  const refs = {
    checks: /** @type {HTMLElement} */ ($('#checks')),
    allOn: /** @type {HTMLButtonElement} */ ($('#allOn')),
    allOff: /** @type {HTMLButtonElement} */ ($('#allOff')),
    invert: /** @type {HTMLButtonElement} */ ($('#invert')),
    add: /** @type {HTMLButtonElement} */ ($('#add')),
    newName: /** @type {HTMLInputElement} */ ($('#newName')),
    newWeight: /** @type {HTMLInputElement} */ ($('#newWeight')),
    start: /** @type {HTMLButtonElement} */ ($('#start')),
    stop: /** @type {HTMLButtonElement} */ ($('#stop')),
    wheel: /** @type {HTMLCanvasElement} */ ($('#wheel')),
    current: /** @type {HTMLElement} */ ($('#current')),
    presets: /** @type {HTMLElement} */ ($('#presets')),
    favorites: /** @type {HTMLElement} */ ($('#favorites')),
    maxTime: /** @type {HTMLInputElement} */ ($('#maxTime')),
    decelTime: /** @type {HTMLInputElement} */ ($('#decelTime')),
    installApp: /** @type {HTMLButtonElement} */ ($('#installApp')),
    installHint: /** @type {HTMLElement} */ ($('#installHint'))
  };

  const wheel = createWheel(refs.wheel, refs.current);
  /** @type {CharacterListUI | undefined} */
  let ui;
  const animation = new WheelAnimationController(wheel, {
    onStateChange: (running, info) => {
      if (ui) {
        ui.updateStartButton();
        ui.setStopButtonEnabled(running && info.stopEnabled);
      }
    },
    onFinalize: (winner) => {
      if (ui) {
        ui.showWinner(winner);
        ui.setStopButtonEnabled(false);
      }
    }
  });

  ui = new CharacterListUI(
    {
      checks: refs.checks,
      allOn: refs.allOn,
      allOff: refs.allOff,
      invert: refs.invert,
      add: refs.add,
      newName: refs.newName,
      newWeight: refs.newWeight,
      start: refs.start,
      stop: refs.stop,
      current: refs.current,
      presets: refs.presets,
      favorites: refs.favorites
    },
    { wheel, animation }
  );
  ui.init();
  ui.setStopButtonEnabled(false);
  ui.refreshFavoriteButtons();

  const startHandler = (event) => {
    event.preventDefault();
    if (!wheel.hasSegments) {
      wheel.rebuild(getActiveEntries());
    }
    if (!wheel.hasSegments) {
      ui.updateStartButton();
      return;
    }
    const result = animation.start(refs.maxTime.value, refs.decelTime.value);
    if (result) {
      refs.maxTime.value = result.total.toFixed(1);
      refs.decelTime.value = result.decel.toFixed(1);
    }
  };
  refs.start.addEventListener('click', startHandler);
  refs.start.addEventListener('pointerdown', startHandler, { passive: false });
  refs.start.addEventListener('touchstart', startHandler, { passive: false });

  refs.stop.addEventListener('click', () => {
    animation.requestDecel(refs.decelTime.value);
  });

  ui.bindPresetHandler((value) => {
    if (applyPreset(value)) {
      ui.renderChecks();
      wheel.rebuild(getActiveEntries());
      ui.updateStartButton();
      queueSaveStateToCookie();
    }
  });

  const { loaded, matchedPreset } = loadStateFromCookie();
  if (loaded) {
    ui.renderChecks();
    wheel.rebuild(getActiveEntries());
    ui.updateStartButton();
    if (matchedPreset) {
      ui.selectPresetRadio(matchedPreset);
    }
  } else {
    const defaultPreset = 'オラタン';
    ui.selectPresetRadio(defaultPreset);
    if (applyPreset(defaultPreset)) {
      ui.renderChecks();
      wheel.rebuild(getActiveEntries());
      ui.updateStartButton();
    }
  }

  ui.refreshFavoriteButtons();
  queueSaveStateToCookie();
  window.requestAnimationFrame(() => wheel.resize());

  setupPwaInstall(refs.installApp, refs.installHint);
}

document.addEventListener('DOMContentLoaded', init, { once: true });

const params = new URLSearchParams(window.location.search);
const isDesktopHost = params.get('desktop') === '1';

if (!isDesktopHost && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker
      .register('./sw.js')
      .then((registration) => {
        console.info('Service Worker registration succeeded', registration);
      })
      .catch((err) => {
        console.error('SW registration failed', err);
        const reasonParts = [];
        if (err && err.name) reasonParts.push(err.name);
        if (err && err.message) reasonParts.push(err.message);
        const reason = reasonParts.length ? reasonParts.join(': ') : String(err);
        alert(`Service Worker の登録に失敗しました。\n理由: ${reason}`);
      });
  });
}

/**
 * PWAインストール導線を初期化する。
 * @param {HTMLButtonElement} installButton
 * @param {HTMLElement} hintEl
 */
function setupPwaInstall(installButton, hintEl) {
  /** @type {BeforeInstallPromptEvent|null} */
  let deferredPrompt = null;

  const inStandalone = window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
  if (inStandalone) {
    hintEl.textContent = 'インストール済みのアプリとして起動中です。';
    return;
  }

  installButton.hidden = true;
  installButton.disabled = true;

  window.addEventListener('beforeinstallprompt', (event) => {
    event.preventDefault();
    deferredPrompt = /** @type {BeforeInstallPromptEvent} */ (event);
    installButton.hidden = false;
    installButton.disabled = false;
    hintEl.textContent = '「アプリをインストール」でホーム画面に追加できます。';
  });

  installButton.addEventListener('click', async () => {
    if (!deferredPrompt) {
      return;
    }
    installButton.disabled = true;
    await deferredPrompt.prompt();
    const choice = await deferredPrompt.userChoice;
    deferredPrompt = null;
    installButton.hidden = true;
    hintEl.textContent = choice.outcome === 'accepted'
      ? 'インストールを開始しました。'
      : 'インストールはキャンセルされました。';
  });

  window.addEventListener('appinstalled', () => {
    installButton.hidden = true;
    installButton.disabled = true;
    hintEl.textContent = 'アプリをインストールしました。';
  });

  const ua = window.navigator.userAgent || '';
  const isIos = /iPhone|iPad|iPod/i.test(ua);
  if (isIos) {
    hintEl.textContent = 'iOSは共有メニューから「ホーム画面に追加」でインストールできます。';
  } else {
    hintEl.textContent = 'ブラウザのメニューからもアプリをインストールできます。';
  }
}
