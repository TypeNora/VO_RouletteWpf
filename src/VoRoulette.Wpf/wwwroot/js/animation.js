/**
 * @file Controls requestAnimationFrame driven rotation of the wheel.
 * Isolated from DOM concerns so UI layers can react via callbacks.
 */

import { WheelRenderer } from './wheel.js';

/**
 * @typedef {Object} AnimationStateChange
 * @property {boolean} stopEnabled 停止ボタンを有効化するか。
 */

/**
 * ルーレットの回転を制御するアニメーション管理クラス。
 */
export class WheelAnimationController {
  /**
   * @param {WheelRenderer} wheel 描画対象のホイール。
   * @param {{
   *   onStateChange?: (running: boolean, info: AnimationStateChange) => void,
   *   onFinalize?: (winnerName: string) => void,
   * }} [options]
   */
  constructor(wheel, options = {}) {
    this.wheel = wheel;
    this.running = false;
    this.frameId = 0;
    this.last = 0;
    this.autoDecelId = 0;
    this.autoStopId = 0;
    this.loopCallback = (time) => this.loop(time);
    this.onStateChange = options.onStateChange || (() => {});
    this.onFinalize = options.onFinalize || (() => {});
    this.currentDecel = 1;
  }

  /**
   * アニメーションを開始する。
   * @param {number|string} maxTime 最大回転時間（秒）。
   * @param {number|string} decelTime 減速時間（秒）。
   * @returns {{ total: number, decel: number } | null} 適用した値。開始できない場合は null。
   */
  start(maxTime, decelTime) {
    if (this.running || !this.wheel.hasSegments) {
      return null;
    }
    const total = clamp(parseFloat(String(maxTime)), 1, 20);
    let decel = clamp(parseFloat(String(decelTime)), 0.2, 3);
    if (decel >= total) {
      decel = Math.max(0.2, total * 0.4);
    }

    this.running = true;
    this.wheel.rotation = Math.random() * Math.PI * 2;
    this.wheel.omega = 10 + Math.random() * 4; // 10〜14 rad/s
    this.wheel.decelEndAt = 0;
    this.last = performance.now();
    this.currentDecel = decel;

    if (this.frameId) {
      window.cancelAnimationFrame(this.frameId);
    }
    this.frameId = window.requestAnimationFrame(this.loopCallback);

    window.clearTimeout(this.autoDecelId);
    window.clearTimeout(this.autoStopId);
    this.autoDecelId = window.setTimeout(
      () => this.requestDecel(decel),
      Math.max(0, (total - decel)) * 1000
    );
    this.autoStopId = window.setTimeout(() => this.stop(), total * 1000);

    this.onStateChange(true, { stopEnabled: true });

    return { total, decel };
  }

  /**
   * requestAnimationFrame ループ。
   * @param {number} now performance.now() の値。
   */
  loop(now) {
    if (!this.running) {
      this.frameId = 0;
      return;
    }
    const delta = Math.max(0, Math.min(0.05, (now - this.last) / 1000));
    this.last = now;

    if (this.wheel.decelEndAt) {
      const remaining = (this.wheel.decelEndAt - now) / 1000;
      const duration = this.wheel.decelDur || this.currentDecel;
      this.wheel.omega = remaining > 0 ? this.wheel.omega0 * (remaining / duration) : 0;
      if (remaining <= 0) {
        this.finishAnimation();
        return;
      }
    }

    this.wheel.rotation = (this.wheel.rotation + this.wheel.omega * delta) % (Math.PI * 2);
    this.wheel.draw();
    this.frameId = window.requestAnimationFrame(this.loopCallback);
  }

  /**
   * 減速をリクエストする。
   * @param {number|string} [decelTime] 指定しない場合は直前の値を使用。
   * @returns {boolean} 処理を受け付けたか。
   */
  requestDecel(decelTime) {
    if (!this.running || this.wheel.decelEndAt) {
      return false;
    }
    let duration = typeof decelTime === 'number' ? decelTime : parseFloat(String(decelTime));
    if (Number.isNaN(duration)) {
      duration = this.currentDecel;
    }
    duration = clamp(duration, 0.2, 3);
    this.currentDecel = duration;
    this.wheel.decelDur = duration;
    this.wheel.omega0 = Math.max(2, this.wheel.omega);
    this.wheel.decelEndAt = performance.now() + duration * 1000;
    this.onStateChange(true, { stopEnabled: false });
    return true;
  }

  /**
   * 外部から停止指示を受けた際の補助メソッド。
   * @returns {boolean} 停止処理が始まったか。
   */
  stop() {
    return this.requestDecel(this.currentDecel);
  }

  /**
   * 完全停止時の後処理。
   * @private
   */
  finishAnimation() {
    this.running = false;
    if (this.frameId) {
      window.cancelAnimationFrame(this.frameId);
      this.frameId = 0;
    }
    window.clearTimeout(this.autoDecelId);
    window.clearTimeout(this.autoStopId);
    this.autoDecelId = 0;
    this.autoStopId = 0;
    this.wheel.decelEndAt = 0;

    let rotation = this.wheel.rotation + (Math.random() - 0.5) * (Math.PI / 90);
    rotation = ((rotation % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2);
    this.wheel.rotation = rotation;
    const winner = this.wheel.pickCurrent();
    this.wheel.draw();

    this.onStateChange(false, { stopEnabled: false });
    this.onFinalize(winner ? winner.name : '');
  }
}

/**
 * @param {number} value 入力値。
 * @param {number} min 最小値。
 * @param {number} max 最大値。
 * @returns {number}
 */
function clamp(value, min, max) {
  const num = Number.isNaN(value) ? min : value;
  return Math.min(max, Math.max(min, num));
}
