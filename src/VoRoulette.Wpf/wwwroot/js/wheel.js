/**
 * @file Canvas based roulette wheel renderer.
 * Responsible for building segment geometry, drawing the canvas contents
 * and reacting to layout changes that require resizing the drawing buffer.
 */

const TAU = Math.PI * 2;

/**
 * @typedef {Object} WheelSegment
 * @property {string} name 表示名。
 * @property {number} start 開始角度(rad)。
 * @property {number} end 終了角度(rad)。
 * @property {string} color 表示色。 
 */

export class WheelRenderer {
  /**
   * @param {HTMLCanvasElement} canvas 描画対象のキャンバス。
   * @param {HTMLElement} currentDisplay 現在の結果を表示する要素。
   */
  constructor(canvas, currentDisplay) {
    this.canvas = canvas;
    this.currentDisplay = currentDisplay;
    /** @type {CanvasRenderingContext2D|null} */
    this.ctx = null;
    this.dpr = Math.max(1, window.devicePixelRatio || 1);
    this.size = 520;
    this.radius = 220;
    this.rotation = 0;
    this.omega = 0;
    this.omega0 = 0;
    this.decelDur = 0;
    this.decelEndAt = 0;
    /** @type {WheelSegment[]} */
    this.segments = [];
  }

  /** @returns {boolean} 有効なセグメントが存在するか。 */
  get hasSegments() {
    return this.segments.length > 0;
  }

  /** @private */
  initContext() {
    if (!this.ctx) {
      this.ctx = this.canvas.getContext('2d');
    }
  }

  /**
   * 角度に応じたパステル調の色を返す。
   * @param {number} rad ラジアン角。
   * @returns {string} CSSカラー文字列。
   */
  hueColor(rad) {
    const deg = (rad * 180) / Math.PI;
    const hue = ((deg + 150) % 360 + 360) % 360;
    return `hsl(${hue}, 70%, 75%)`;
  }

  /**
   * 状態からセグメントを再構築し描画する。
   * @param {{name: string, weight: number}[]} entries 有効キャラ一覧。
   */
  rebuild(entries) {
    this.initContext();
    const ctx = this.ctx;
    if (!ctx) {
      return;
    }
    if (!entries.length) {
      this.segments = [];
      this.draw();
      return;
    }
    const total = entries.reduce((sum, entry) => sum + entry.weight, 0);
    let angle = 0;
    const segments = [];
    for (const entry of entries) {
      const span = TAU * (entry.weight / total);
      const mid = angle + span / 2;
      segments.push({ name: entry.name, start: angle, end: angle + span, color: this.hueColor(mid) });
      angle += span;
    }
    this.segments = segments;
    this.draw();
  }

  /**
   * 現在ポインタが指しているセグメントを返す。
   * @returns {WheelSegment|null}
   */
  pickCurrent() {
    if (!this.segments.length) {
      return null;
    }
    let angle = (-this.rotation - Math.PI / 2) % TAU;
    if (angle < 0) {
      angle += TAU;
    }
    return (
      this.segments.find((segment) => angle >= segment.start && angle < segment.end) ||
      this.segments[this.segments.length - 1] ||
      null
    );
  }

  /** キャンバスを描画し現在の文字列も更新する。 */
  draw() {
    this.initContext();
    const ctx = this.ctx;
    if (!ctx) {
      return;
    }
    const { size, dpr, radius } = this;
    if (typeof ctx.reset === 'function') {
      try {
        ctx.reset();
      } catch (err) {
        // Safari などで reset 未対応の場合は無視
      }
    }
    ctx.save();
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, size, size);
    ctx.translate(size / 2, size / 2);
    ctx.rotate(this.rotation);

    ctx.lineWidth = 2;
    for (const segment of this.segments) {
      ctx.beginPath();
      ctx.moveTo(0, 0);
      ctx.arc(0, 0, radius, segment.start, segment.end, false);
      ctx.closePath();
      ctx.fillStyle = segment.color;
      ctx.fill();
      ctx.strokeStyle = '#fff';
      ctx.stroke();

      const mid = (segment.start + segment.end) / 2;
      const label = segment.name || '';
      if (label) {
        ctx.save();
        ctx.rotate(mid);
        const fontSize = Math.max(12, Math.min(24, radius * 0.085));
        const inner = Math.min(radius * 0.2, radius * 0.5);
        const outer = Math.max(inner + fontSize, radius - fontSize * 0.5);
        const available = outer - inner;
        ctx.fillStyle = '#111';
        ctx.font = `600 ${fontSize}px system-ui, Noto Sans JP, sans-serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        const maxWidth = Math.max(available * 0.9, fontSize);
        const metrics = ctx.measureText(label);
        const labelWidth = Math.max(metrics.width, 1);
        const scale = Math.min(1, maxWidth / labelWidth);
        const distance = inner + available / 2;
        ctx.translate(distance, 0);
        if (scale < 1) {
          ctx.scale(scale, scale);
        }
        ctx.fillText(label, 0, 0);
        ctx.restore();
      }
    }

    ctx.beginPath();
    ctx.arc(0, 0, radius * 0.1, 0, TAU);
    ctx.fillStyle = '#fff';
    ctx.fill();
    ctx.strokeStyle = '#222';
    ctx.stroke();
    ctx.restore();

    const current = this.pickCurrent();
    this.currentDisplay.textContent = current ? current.name : '';
  }

  /**
   * レイアウトに応じてキャンバスの実ピクセル数を調整する。
   * @param {number} [retry=0] 親幅0時の再試行回数。
   */
  resize(retry = 0) {
    const parent = this.canvas.parentElement;
    const parentWidth = parent ? parent.clientWidth : 0;
    if (parentWidth === 0 && retry < 10) {
      window.requestAnimationFrame(() => this.resize(retry + 1));
      return;
    }
    const dpr = Math.max(1, window.devicePixelRatio || 1);
    const cssSize = Math.min(parentWidth || 320, 520);
    const pixels = Math.round(cssSize * dpr);
    if (this.canvas.width !== pixels || this.canvas.height !== pixels) {
      this.canvas.width = pixels;
      this.canvas.height = pixels;
    }
    this.size = Math.round(cssSize);
    this.radius = Math.round(cssSize * 0.43);
    this.dpr = dpr;
    this.draw();
  }
}

/**
 * WheelRenderer の生成とリサイズイベント監視をまとめるファクトリ。
 * @param {HTMLCanvasElement} canvas 描画対象。
 * @param {HTMLElement} currentDisplay 結果表示用要素。
 * @returns {WheelRenderer}
 */
export function createWheel(canvas, currentDisplay) {
  const wheel = new WheelRenderer(canvas, currentDisplay);
  const handleResize = () => wheel.resize();
  window.addEventListener('resize', handleResize, { passive: true });
  window.addEventListener('orientationchange', handleResize, { passive: true });
  return wheel;
}
