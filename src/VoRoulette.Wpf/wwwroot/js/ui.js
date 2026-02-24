/**
 * @file Builds and updates the checklist UI for editing characters.
 * Contains DOM related logic and delegates state mutations to state.js helpers.
 */

import {
  state,
  addCharacter,
  updateWeight,
  setActive,
  updateAllActive,
  renameCharacter,
  removeCharacter,
  getActiveEntries,
  queueSaveStateToCookie,
  findMatchingPresetKey,
  saveFavorite,
  loadFavorite,
  hasFavorite,
  FAVORITE_SLOT_COUNT
} from './state.js';

/**
 * @typedef {Object} UIRefs
 * @property {HTMLElement} checks
 * @property {HTMLButtonElement} allOn
 * @property {HTMLButtonElement} allOff
 * @property {HTMLButtonElement} invert
 * @property {HTMLButtonElement} add
 * @property {HTMLInputElement} newName
 * @property {HTMLInputElement} newWeight
 * @property {HTMLButtonElement} start
 * @property {HTMLButtonElement} stop
 * @property {HTMLElement} current
 * @property {HTMLElement} presets
 * @property {HTMLElement} favorites
 */

/**
 * キャラクター編集UIを管理するクラス。
 */
export class CharacterListUI {
  /**
   * @param {UIRefs} refs DOM参照群。
   * @param {{ wheel: import('./wheel.js').WheelRenderer, animation: import('./animation.js').WheelAnimationController }} deps 依存オブジェクト。
   */
  constructor(refs, { wheel, animation }) {
    this.refs = refs;
    this.wheel = wheel;
    this.animation = animation;
    /** @type {HTMLButtonElement[]} */
    this.favoriteButtons = [];
    /** @type {HTMLInputElement[]} */
    this.favoriteModeInputs = [];
    /** @type {HTMLElement|null} */
    this.favoriteStatusEl = null;
  }

  /** 初期化して各種イベントをバインドする。 */
  init() {
    this.refs.add.addEventListener('click', () => this.handleAdd());
    this.refs.allOn.addEventListener('click', () => this.updateSelections(() => true));
    this.refs.allOff.addEventListener('click', () => this.updateSelections(() => false));
    this.refs.invert.addEventListener('click', () => this.updateSelections((prev) => !prev));
    this.setupFavoriteControls();
  }

  /** お気に入りボタン関連の初期化 */
  setupFavoriteControls() {
    const container = this.refs.favorites;
    const buttonsContainer = container.querySelector('.favorite-buttons');
    if (!buttonsContainer) {
      throw new Error('Favorite buttons container not found.');
    }
    this.ensureFavoriteButtonSlots(buttonsContainer);
    this.favoriteButtons = Array.from(buttonsContainer.querySelectorAll('button[data-favorite-slot]'));
    this.favoriteModeInputs = Array.from(container.querySelectorAll('input[name="favoriteMode"]'));
    this.favoriteStatusEl = container.querySelector('[data-favorite-status]');
    this.favoriteButtons.forEach((button) => {
      button.addEventListener('click', () => {
        const slot = Number(button.dataset.favoriteSlot);
        if (!Number.isInteger(slot)) {
          return;
        }
        this.handleFavoriteButtonClick(slot);
      });
    });
    this.favoriteModeInputs.forEach((input) => {
      input.addEventListener('change', () => {
        if (input.checked) {
          this.setFavoriteStatus(input.value === 'save' ? '保存モードに切り替えました。' : '読込モードに切り替えました。');
        }
        this.refreshFavoriteButtons();
      });
    });
    this.refreshFavoriteButtons();
    this.setFavoriteStatus('');
  }

  /**
   * HTMLに存在するお気に入りボタン数をスロット数と同期する。
   * @param {Element} buttonsContainer ボタンを内包する要素。
   */
  ensureFavoriteButtonSlots(buttonsContainer) {
    const buttons = Array.from(buttonsContainer.querySelectorAll('button[data-favorite-slot]'));
    // 余分なボタンは削除する
    for (let i = buttons.length - 1; i >= FAVORITE_SLOT_COUNT; i -= 1) {
      buttons[i].remove();
    }
    // 残ったボタンの番号と表示を整える
    const syncedButtons = Array.from(buttonsContainer.querySelectorAll('button[data-favorite-slot]'));
    syncedButtons.forEach((button, index) => {
      const displayIndex = index + 1;
      button.dataset.favoriteSlot = String(index);
      button.textContent = String(displayIndex);
      button.setAttribute('aria-label', `お気に入り${displayIndex}`);
    });
    // 足りない分を追加する
    for (let i = syncedButtons.length; i < FAVORITE_SLOT_COUNT; i += 1) {
      const button = document.createElement('button');
      button.type = 'button';
      button.dataset.favoriteSlot = String(i);
      const displayIndex = i + 1;
      button.textContent = String(displayIndex);
      button.setAttribute('aria-label', `お気に入り${displayIndex}`);
      buttonsContainer.appendChild(button);
    }
  }

  /** 現在のモードを取得する */
  getFavoriteMode() {
    const active = this.favoriteModeInputs.find((input) => input.checked);
    return active && active.value === 'save' ? 'save' : 'load';
  }

  /** お気に入りボタンの表示を更新する */
  refreshFavoriteButtons() {
    const mode = this.getFavoriteMode();
    this.refs.favorites.dataset.mode = mode;
    this.favoriteButtons.forEach((button) => {
      const slot = Number(button.dataset.favoriteSlot);
      if (!Number.isInteger(slot)) {
        return;
      }
      const saved = hasFavorite(slot);
      const displayIndex = slot + 1;
      button.classList.toggle('saved', saved);
      button.setAttribute('aria-pressed', saved ? 'true' : 'false');
      button.title = saved ? `お気に入り${displayIndex}：保存済み` : `お気に入り${displayIndex}：未保存`;
      button.disabled = mode === 'load' && !saved;
    });
  }

  /** お気に入り操作のステータスメッセージを更新する */
  setFavoriteStatus(message) {
    if (this.favoriteStatusEl) {
      this.favoriteStatusEl.textContent = message;
    }
  }

  /** お気に入りボタンのクリック処理 */
  handleFavoriteButtonClick(slot) {
    const mode = this.getFavoriteMode();
    const displayIndex = slot + 1;
    if (mode === 'save') {
      const saved = saveFavorite(slot);
      this.refreshFavoriteButtons();
      this.setFavoriteStatus(saved ? `お気に入り${displayIndex}に保存しました。` : 'お気に入りの保存に失敗しました。');
      return;
    }

    if (loadFavorite(slot)) {
      this.renderChecks();
      this.updateWheel();
      const matched = findMatchingPresetKey();
      this.selectPresetRadio(matched);
      this.setFavoriteStatus(`お気に入り${displayIndex}を読み込みました。`);
    } else {
      this.setFavoriteStatus(`お気に入り${displayIndex}は未保存です。`);
    }
    this.refreshFavoriteButtons();
  }

  /**
   * キャラクター一覧を再描画する。
   */
  renderChecks() {
    this.refs.checks.innerHTML = '';
    const frag = document.createDocumentFragment();
    for (const name of state.names) {
      frag.appendChild(this.createRow(name));
    }
    this.refs.checks.append(frag);
    this.updateStartButton();
  }

  /**
   * 行要素を生成する。
   * @param {string} name キャラクター名。
   * @returns {HTMLElement}
   */
  createRow(name) {
    const row = document.createElement('div');
    row.className = 'row';

    const checkbox = document.createElement('input');
    checkbox.type = 'checkbox';
    checkbox.checked = !!state.on[name];
    checkbox.dataset.name = name;
    checkbox.addEventListener('change', () => {
      setActive(name, checkbox.checked);
      this.updateWheel();
      queueSaveStateToCookie();
    });

    const nameInput = document.createElement('input');
    nameInput.className = 'name';
    nameInput.value = name;
    nameInput.addEventListener('change', () => {
      const renamed = renameCharacter(name, nameInput.value);
      this.renderChecks();
      if (renamed) {
        this.updateWheel();
        queueSaveStateToCookie();
      }
    });

    const weightInput = document.createElement('input');
    weightInput.className = 'weight';
    weightInput.type = 'number';
    weightInput.min = '0.1';
    weightInput.max = '10';
    weightInput.step = 'any';
    const initialWeight = Object.prototype.hasOwnProperty.call(state.weight, name)
      ? state.weight[name]
      : updateWeight(name, 1);
    weightInput.value = String(initialWeight);
    weightInput.inputMode = 'decimal';
    weightInput.setAttribute('aria-label', '重み');
    weightInput.title = '重み';
    weightInput.addEventListener('input', () => {
      const normalized = updateWeight(name, weightInput.value);
      weightInput.value = String(normalized);
      this.updateWheel();
    });
    weightInput.addEventListener('change', () => {
      queueSaveStateToCookie();
    });

    const removeButton = document.createElement('button');
    removeButton.className = 'remove';
    removeButton.textContent = '削除';
    removeButton.addEventListener('click', () => {
      if (removeCharacter(name)) {
        this.renderChecks();
        this.updateWheel();
        queueSaveStateToCookie();
      }
    });

    row.append(checkbox, nameInput, weightInput, removeButton);
    return row;
  }

  /**
   * 新規追加処理。
   */
  handleAdd() {
    const name = (this.refs.newName.value || '').trim();
    if (!name) {
      return;
    }
    const weight = this.refs.newWeight.value || '1';
    if (addCharacter(name, weight)) {
      this.refs.newName.value = '';
      this.refs.newWeight.value = '1';
      this.renderChecks();
      this.updateWheel();
      queueSaveStateToCookie();
    }
  }

  /**
   * 全選択／全解除など一括更新。
   * @param {(prev: boolean, name: string) => boolean} mapper 次の状態を返す関数。
   */
  updateSelections(mapper) {
    const changed = updateAllActive(mapper);
    if (changed) {
      const checkboxes = this.refs.checks.querySelectorAll('input[type="checkbox"][data-name]');
      checkboxes.forEach((cb) => {
        const name = cb.dataset.name;
        if (!name) {
          return;
        }
        cb.checked = !!state.on[name];
      });
      this.updateWheel();
      queueSaveStateToCookie();
    }
  }

  /** Wheel と開始ボタン状態を更新する。 */
  updateWheel() {
    this.wheel.rebuild(getActiveEntries());
    this.updateStartButton();
  }

  /**
   * スタートボタンの有効・無効を切り替える。
   */
  updateStartButton() {
    this.refs.start.disabled = !this.wheel.hasSegments || this.animation.running;
  }

  /**
   * ストップボタンの有効状態を設定する。
   * @param {boolean} enabled 有効化するか。
   */
  setStopButtonEnabled(enabled) {
    this.refs.stop.disabled = !enabled;
  }

  /**
   * 当選表示をポップさせる。
   * @param {string} name 表示する名称。
   */
  showWinner(name) {
    const target = this.refs.current;
    target.textContent = name || '';
    target.classList.remove('pop');
    void target.offsetWidth; // アニメーション再適用のためのリフロー
    target.classList.add('pop');
  }

  /**
   * プリセットラジオのチェックを更新する。
   * @param {string|null} value チェックする値。
   */
  selectPresetRadio(value) {
    const radios = this.refs.presets.querySelectorAll('input[name="preset"]');
    radios.forEach((radio) => {
      radio.checked = radio.value === value;
    });
  }

  /**
   * プリセットラジオへイベントを設定する。
   * @param {(value: string) => void} handler 変更時の処理。
   */
  bindPresetHandler(handler) {
    const radios = this.refs.presets.querySelectorAll('input[name="preset"]');
    radios.forEach((radio) => {
      radio.addEventListener('change', () => handler(radio.value));
    });
  }
}
