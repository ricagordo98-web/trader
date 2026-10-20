const charts = new Map();

const periods = {
    '1m': { span: 1, type: 'minute' },
    '2m': { span: 2, type: 'minute' },
    '5m': { span: 5, type: 'minute' },
    '15m': { span: 15, type: 'minute' },
    '30m': { span: 30, type: 'minute' },
    '60m': { span: 60, type: 'minute' },
    '1d': { span: 1, type: 'day' },
    '1wk': { span: 1, type: 'week' },
    '1mo': { span: 1, type: 'month' }
};

let rectangleRegistered = false;

function registerRectangle() {
    if (rectangleRegistered) {
        return;
    }

    window.klinecharts.registerOverlay({
        name: 'rectangle',
        totalStep: 3,
        needDefaultPointFigure: true,
        needDefaultXAxisFigure: true,
        needDefaultYAxisFigure: true,
        createPointFigures: ({ coordinates }) => {
            if (coordinates.length < 2) {
                return [];
            }

            const [a, b] = coordinates;
            return {
                type: 'rect',
                attrs: {
                    x: Math.min(a.x, b.x),
                    y: Math.min(a.y, b.y),
                    width: Math.abs(a.x - b.x),
                    height: Math.abs(a.y - b.y)
                },
                styles: {
                    style: 'stroke_fill',
                    color: 'rgba(76, 141, 255, 0.15)',
                    borderColor: '#4C8DFF',
                    borderSize: 1
                }
            };
        }
    });

    rectangleRegistered = true;
}

function toBar(candle) {
    return {
        timestamp: typeof candle.time === 'number' ? candle.time : Date.parse(candle.time),
        open: candle.open,
        high: candle.high,
        low: candle.low,
        close: candle.close,
        volume: candle.volume
    };
}

function notifyDrawingsChanged(state) {
    if (state.suspended) {
        return;
    }

    state.dotnet.invokeMethodAsync('OnDrawingsChanged', JSON.stringify(serializeDrawings(state)));
}

function serializeDrawings(state) {
    return state.chart.getOverlays()
        .filter(o => state.drawingIds.has(o.id))
        .map(o => ({ name: o.name, points: o.points.map(p => ({ timestamp: p.timestamp, value: p.value })) }));
}

function createDrawing(state, definition) {
    const id = state.chart.createOverlay({
        name: definition.name,
        points: definition.points,
        onDrawEnd: () => notifyDrawingsChanged(state),
        onPressedMoveEnd: () => notifyDrawingsChanged(state),
        onSelected: ({ overlay }) => { state.selectedId = overlay.id; },
        onDeselected: () => { state.selectedId = null; },
        onRemoved: ({ overlay }) => {
            state.drawingIds.delete(overlay.id);
            notifyDrawingsChanged(state);
        }
    });

    if (id) {
        state.drawingIds.add(id);
    }
}

export function create(id, container, dotnet, dark, showVolume) {
    registerRectangle();

    const chart = window.klinecharts.init(container, { timezone: 'Europe/Lisbon' });
    const state = {
        chart,
        dotnet,
        data: [],
        pushCallback: null,
        indicators: new Map(),
        drawingIds: new Set(),
        levelIds: [],
        selectedId: null,
        suspended: false,
        container,
        onKeyDown: null
    };

    chart.setStyles(dark ? 'dark' : 'light');
    chart.setDataLoader({
        subscribeBar: ({ callback }) => { state.pushCallback = callback; },
        unsubscribeBar: () => { state.pushCallback = null; },
        getBars: ({ type, callback }) => {
            callback(type === 'init' ? state.data : [], false);
        }
    });

    state.onKeyDown = e => {
        if (e.key === 'Delete' && state.selectedId) {
            chart.removeOverlay({ id: state.selectedId });
            state.selectedId = null;
        }
    };
    document.addEventListener('keydown', state.onKeyDown);

    charts.set(id, state);
    if (showVolume) {
        chart.createIndicator('VOL', false, { id: 'vol_pane', height: 90 });
        state.indicators.set('VOL', 'vol_pane');
    }
}

export function setData(id, symbol, pricePrecision, interval, candles) {
    const state = charts.get(id);
    state.data = candles.map(toBar);
    state.chart.setSymbol({ ticker: symbol, pricePrecision, volumePrecision: 0 });
    state.chart.setPeriod(periods[interval] ?? periods['1d']);
    state.chart.resetData();
}

export function pushBar(id, candle) {
    const state = charts.get(id);
    if (!state || state.data.length === 0) {
        return;
    }

    const bar = toBar(candle);
    const last = state.data[state.data.length - 1];

    if (bar.timestamp < last.timestamp) {
        return;
    }

    if (bar.timestamp === last.timestamp) {
        state.data[state.data.length - 1] = bar;
    } else {
        state.data.push(bar);
    }

    state.pushCallback?.(bar);
}

const periodMs = { '1m': 60000, '2m': 120000, '5m': 300000, '15m': 900000, '30m': 1800000, '60m': 3600000 };

export function pushTick(id, interval, price, time) {
    const state = charts.get(id);
    if (!state || state.data.length === 0) {
        return;
    }

    const last = state.data[state.data.length - 1];
    const ms = periodMs[interval];
    let bar;

    if (ms && time >= last.timestamp + ms) {
        const timestamp = last.timestamp + Math.floor((time - last.timestamp) / ms) * ms;
        bar = { timestamp, open: price, high: price, low: price, close: price, volume: 0 };
        state.data.push(bar);
    } else {
        bar = { ...last, close: price, high: Math.max(last.high, price), low: Math.min(last.low, price) };
        state.data[state.data.length - 1] = bar;
    }

    state.pushCallback?.(bar);
}

export function setTheme(id, dark) {
    charts.get(id)?.chart.setStyles(dark ? 'dark' : 'light');
}

export function toggleIndicator(id, name, mainPane) {
    const state = charts.get(id);

    if (state.indicators.has(name)) {
        state.chart.removeIndicator({ paneId: state.indicators.get(name), name });
        state.indicators.delete(name);
    } else {
        const paneId = state.chart.createIndicator(name, mainPane, mainPane ? { id: 'candle_pane' } : undefined);
        state.indicators.set(name, mainPane ? 'candle_pane' : paneId);
    }

    return Array.from(state.indicators.keys());
}

export function startDrawing(id, name) {
    const state = charts.get(id);
    const overlayId = state.chart.createOverlay({
        name,
        onDrawEnd: () => notifyDrawingsChanged(state),
        onPressedMoveEnd: () => notifyDrawingsChanged(state),
        onSelected: ({ overlay }) => { state.selectedId = overlay.id; },
        onDeselected: () => { state.selectedId = null; },
        onRemoved: ({ overlay }) => {
            state.drawingIds.delete(overlay.id);
            notifyDrawingsChanged(state);
        }
    });

    if (overlayId) {
        state.drawingIds.add(overlayId);
    }
}

export function loadDrawings(id, drawings) {
    const state = charts.get(id);
    state.suspended = true;

    for (const drawingId of state.drawingIds) {
        state.chart.removeOverlay({ id: drawingId });
    }
    state.drawingIds.clear();

    for (const drawing of drawings) {
        createDrawing(state, drawing);
    }

    state.suspended = false;
}

export function removeSelected(id) {
    const state = charts.get(id);
    if (state.selectedId) {
        state.chart.removeOverlay({ id: state.selectedId });
        state.selectedId = null;
    }
}

export function clearDrawings(id) {
    const state = charts.get(id);
    state.suspended = true;

    for (const drawingId of state.drawingIds) {
        state.chart.removeOverlay({ id: drawingId });
    }
    state.drawingIds.clear();

    state.suspended = false;
    notifyDrawingsChanged(state);
}

export function resize(id) {
    charts.get(id)?.chart.resize();
}

export function dispose(id) {
    const state = charts.get(id);
    if (!state) {
        return;
    }

    document.removeEventListener('keydown', state.onKeyDown);
    window.klinecharts.dispose(state.container);
    charts.delete(id);
}

export function setLevels(id, levels) {
    const state = charts.get(id);

    for (const levelId of state.levelIds) {
        state.chart.removeOverlay({ id: levelId });
    }
    state.levelIds = [];

    const timestamp = state.data.length > 0 ? state.data[state.data.length - 1].timestamp : undefined;

    for (const level of levels) {
        const overlayId = state.chart.createOverlay({
            name: 'horizontalStraightLine',
            points: [{ timestamp, value: level.price }],
            lock: true,
            styles: { line: { color: level.color, style: 'dashed', dashedValue: [6, 4], size: 1 } }
        });

        if (overlayId) {
            state.levelIds.push(overlayId);
        }
    }
}
