# -*- coding: utf-8 -*-
import datetime
import json
import math
import os
import sys

sys.path.append(os.path.join(os.path.dirname(__file__), "Lib"))
from ReportUtils import *


DASHBOARD_RECORD_LIMIT = 5000


def sanitize_for_json(value):
    # json.dumps по умолчанию может записать NaN, а это ломает часть браузеров.
    if value is None:
        return None
    if isinstance(value, bool):
        return value
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        if math.isnan(value) or math.isinf(value):
            return None
        return value
    if isinstance(value, str):
        return value.replace("\u2028", " ").replace("\u2029", " ")
    if isinstance(value, list):
        return [sanitize_for_json(item) for item in value]
    if isinstance(value, dict):
        return {str(key): sanitize_for_json(val) for key, val in value.items()}
    return clean_string(value)


def safe_json_for_script(value):
    text = json.dumps(sanitize_for_json(value), ensure_ascii=False, allow_nan=False)
    return text.replace("</", "<\\/").replace("\u2028", " ").replace("\u2029", " ")


def prepare_dashboard_records(df):
    fields = [
        "source", "url", "brand", "model", "price", "year", "sale_region", "mileage",
        "transmission", "drive_type", "color", "body_type", "steering_wheel",
        "engine_power", "engine_volume", "fuel_type", "description",
    ]
    for field in fields:
        if field not in df.columns:
            df[field] = None
    return dataframe_to_records(df[fields].copy(), limit=DASHBOARD_RECORD_LIMIT)


def build_dashboard_html(records, total_records):
    created_at = h(datetime.datetime.now().strftime("%d.%m.%Y %H:%M:%S"))
    embedded_count = len(records)
    total_count_text = format_number(total_records)
    embedded_count_text = format_number(embedded_count)
    truncated_note = ""
    if total_records > embedded_count:
        truncated_note = (
            f"<div class='warning'>В интерактивный отчёт встроено {embedded_count_text} записей из {total_count_text}, "
            "чтобы HTML-файл не стал слишком тяжёлым.</div>"
        )

    template = r'''<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="UTF-8">
<title>AutoScope Dynamic Dashboard</title>
<style>
:root { --bg:#f4f6f8; --card:#fff; --text:#17212b; --muted:#667085; --line:#d9e0e7; --accent:#1f6feb; --warn:#b7791f; --bad:#b42318; }
* { box-sizing:border-box; }
body { margin:0; padding:26px; font-family:Arial, Helvetica, sans-serif; background:var(--bg); color:var(--text); }
a { color:var(--accent); text-decoration:none; }
a:hover { text-decoration:underline; }
.header { background:linear-gradient(135deg,#17212b,#31445a); color:white; border-radius:18px; padding:26px 30px; margin-bottom:20px; }
.header h1 { margin:0 0 10px 0; font-size:30px; }
.header p { margin:0; color:#dbe5ef; line-height:1.45; }
.meta { margin-top:14px; font-size:13px; color:#dbe5ef; }
.card { background:var(--card); border:1px solid var(--line); border-radius:14px; padding:18px; margin-bottom:18px; box-shadow:0 1px 3px rgba(16,24,40,.06); }
.grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(210px,1fr)); gap:14px; margin-bottom:18px; }
.controls { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:12px; }
label { display:block; font-size:12px; color:var(--muted); margin-bottom:5px; }
select,input { width:100%; padding:9px 10px; border:1px solid var(--line); border-radius:10px; background:#fff; color:var(--text); }
button { padding:10px 14px; border:0; border-radius:10px; background:var(--accent); color:white; cursor:pointer; }
.metric { background:white; border:1px solid var(--line); border-radius:14px; padding:16px; }
.metric .label { color:var(--muted); font-size:13px; margin-bottom:8px; }
.metric .value { font-size:24px; font-weight:700; }
.chart-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(430px,1fr)); gap:16px; }
.chart-card { background:white; border:1px solid var(--line); border-radius:14px; padding:14px; min-height:330px; }
.chart-card h3 { margin:0 0 10px 0; font-size:16px; }
canvas { width:100%; height:270px; display:block; }
table { width:100%; border-collapse:collapse; }
th,td { padding:9px; border-bottom:1px solid var(--line); text-align:left; vertical-align:top; font-size:13px; }
th { background:#f7f9fb; color:#344054; }
th.sortable { cursor:pointer; user-select:none; white-space:nowrap; }
th.sortable:hover { background:#eef4ff; color:#174ea6; }
th.sorted { color:var(--accent); }
.sort-mark { color:var(--muted); font-size:11px; margin-left:4px; }
.small { color:var(--muted); font-size:13px; }
.warning { border-left:4px solid var(--warn); background:#fff9ed; padding:10px 12px; border-radius:10px; margin-bottom:16px; }
.info { border-left:4px solid var(--accent); background:#f5f9ff; padding:10px 12px; border-radius:10px; margin-bottom:16px; }
.error { border-left:4px solid var(--bad); background:#fff4f2; padding:10px 12px; border-radius:10px; margin-bottom:16px; display:none; white-space:pre-wrap; }
.price { font-weight:700; white-space:nowrap; }
.checkbox-line { display:flex; align-items:center; gap:8px; margin-top:10px; color:var(--muted); font-size:13px; }
.checkbox-line input { width:auto; }
@media (max-width:720px) { body { padding:14px; } .chart-grid { grid-template-columns:1fr; } }
</style>
</head>
<body>
<div class="header">
  <h1>Интерактивный отчёт AutoScope</h1>
  <p>Фильтры и графики работают прямо в HTML-файле без сервера. Можно быстро смотреть срезы по источнику, бренду, модели, региону, году, цене и пробегу.</p>
  <div class="meta">Сформировано: __CREATED_AT__</div>
</div>
__TRUNCATED_NOTE__
<div id="errorBox" class="error"></div>
<div id="diagnostics" class="info">Загружено записей в отчёт: __EMBEDDED_COUNT__ из __TOTAL_COUNT__.</div>
<div class="card">
  <h2>Фильтры</h2>
  <div class="controls">
    <div><label>Источник</label><select id="sourceFilter"></select></div>
    <div><label>Бренд</label><select id="brandFilter"></select></div>
    <div><label>Модель</label><select id="modelFilter"></select></div>
    <div><label>Регион</label><select id="regionFilter"></select></div>
    <div><label>Цена от</label><input id="priceMin" type="number" placeholder="например 800000"></div>
    <div><label>Цена до</label><input id="priceMax" type="number" placeholder="например 2500000"></div>
    <div><label>Год от</label><input id="yearMin" type="number" placeholder="например 2012"></div>
    <div><label>Год до</label><input id="yearMax" type="number" placeholder="например 2021"></div>
    <div><label>Пробег до</label><input id="mileageMax" type="number" placeholder="например 150000"></div>
    <div><label>Поиск</label><input id="textSearch" type="text" placeholder="бренд, модель, регион"></div>
    <div><label>Интервалы гистограммы цен</label><input id="histogramBins" type="number" value="24" min="6" max="60"></div>
    <div style="display:flex;align-items:end"><button id="resetButton">Сбросить фильтры</button></div>
  </div>
</div>
<div class="grid" id="metrics"></div>
<div class="chart-grid">
  <div class="chart-card"><h3>Распределение цен</h3><canvas id="priceHistogramCanvas"></canvas></div>
  <div class="chart-card"><h3>Топ брендов</h3><canvas id="brandChart"></canvas></div>
  <div class="chart-card"><h3>Топ регионов</h3><canvas id="regionChart"></canvas></div>
  <div class="chart-card"><h3>Медианная цена по годам</h3><canvas id="priceByYearChart"></canvas></div>
  <div class="chart-card"><h3>Цена и пробег</h3><canvas id="scatterChart"></canvas><label class="checkbox-line"><input type="checkbox" id="trendToggle" checked>Показывать линию тренда</label></div>
  <div class="chart-card"><h3>Коробка передач</h3><canvas id="transmissionChart"></canvas></div>
</div>
<div class="card">
  <h2>Таблица объявлений</h2>
  <p class="small" id="tableInfo"></p>
  <div style="overflow-x:auto"><table id="recordsTable"></table></div>
</div>
<script type="application/json" id="autoscope-data">__RECORDS_JSON__</script>
<script>
let rawData = [];
let sortState = { field: 'price', direction: 'asc' };
const tableColumns = [
  { field: 'car', title: 'Автомобиль', type: 'text' },
  { field: 'price', title: 'Цена', type: 'number' },
  { field: 'year', title: 'Год', type: 'number' },
  { field: 'mileage', title: 'Пробег', type: 'number' },
  { field: 'body_type', title: 'Кузов', type: 'text' },
  { field: 'sale_region', title: 'Регион', type: 'text' },
  { field: 'source', title: 'Источник', type: 'text' },
  { field: 'url', title: 'Ссылка', type: 'text' }
];
const fmt = new Intl.NumberFormat('ru-RU');

function parseEmbeddedData() {
  const node = document.getElementById('autoscope-data');
  if (!node) return [];
  const parsed = JSON.parse(node.textContent || '[]');
  return Array.isArray(parsed) ? parsed : [];
}
function clean(value) { return value === null || value === undefined ? '' : String(value).trim(); }
function num(value) {
  if (value === null || value === undefined || value === '') return null;
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  const prepared = String(value).replace(/\s+/g, '').replace(',', '.').replace(/[^0-9.\-]/g, '');
  if (!prepared || prepared === '-' || prepared === '.') return null;
  const n = Number(prepared);
  return Number.isFinite(n) ? n : null;
}
function priceText(value) { const n = num(value); return n === null ? '—' : fmt.format(Math.round(n)) + ' ₽'; }
function numberText(value) { const n = num(value); return n === null ? '—' : fmt.format(Math.round(n)); }
function yearText(value) { const n = num(value); return n === null ? '—' : String(Math.round(n)); }
function median(values) { const arr = values.map(num).filter(v => v !== null).sort((a,b)=>a-b); if (!arr.length) return null; const mid = Math.floor(arr.length / 2); return arr.length % 2 ? arr[mid] : (arr[mid-1] + arr[mid]) / 2; }
function mean(values) { const arr = values.map(num).filter(v => v !== null); if (!arr.length) return null; return arr.reduce((a,b)=>a+b,0) / arr.length; }
function escapeHtml(text) { return clean(text).replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch])); }
function carName(item) { return [item.brand, item.model].map(clean).filter(Boolean).join(' ') || ''; }
function sortValue(item, field, type) {
  if (field === 'car') return carName(item);
  if (type === 'number') return num(item[field]);
  return clean(item[field]).toLowerCase();
}
function compareRecords(a, b) {
  const column = tableColumns.find(c => c.field === sortState.field) || tableColumns[1];
  const av = sortValue(a, column.field, column.type);
  const bv = sortValue(b, column.field, column.type);
  let result = 0;
  if (column.type === 'number') {
    const aMissing = av === null;
    const bMissing = bv === null;
    if (aMissing && bMissing) result = 0;
    else if (aMissing) result = 1;
    else if (bMissing) result = -1;
    else result = av - bv;
  } else {
    result = String(av).localeCompare(String(bv), 'ru', { numeric: true, sensitivity: 'base' });
  }
  return sortState.direction === 'desc' ? -result : result;
}
function sortRecords(data) { return data.slice().sort(compareRecords); }
function sortMark(field) {
  if (sortState.field !== field) return '<span class="sort-mark">↕</span>';
  return sortState.direction === 'asc' ? '<span class="sort-mark">↑</span>' : '<span class="sort-mark">↓</span>';
}
function headerCell(column) {
  const sortedClass = sortState.field === column.field ? ' sortable sorted' : ' sortable';
  return `<th class="${sortedClass}" data-sort="${escapeHtml(column.field)}">${escapeHtml(column.title)}${sortMark(column.field)}</th>`;
}
function changeSort(field) {
  if (sortState.field === field) {
    sortState.direction = sortState.direction === 'asc' ? 'desc' : 'asc';
  } else {
    sortState.field = field;
    sortState.direction = ['price', 'year', 'mileage'].includes(field) ? 'asc' : 'asc';
  }
  update();
}
function bindTableSort() {
  document.querySelectorAll('#recordsTable th[data-sort]').forEach(th => {
    th.addEventListener('click', () => changeSort(th.getAttribute('data-sort')));
  });
}
function sortDescription() {
  const column = tableColumns.find(c => c.field === sortState.field);
  if (!column) return '';
  return ` Сортировка: ${column.title.toLowerCase()}, ${sortState.direction === 'asc' ? 'по возрастанию' : 'по убыванию'}.`;
}
function showError(err) { const box = document.getElementById('errorBox'); box.style.display = 'block'; box.textContent = 'Ошибка интерактивного отчёта:\n' + (err && err.stack ? err.stack : String(err)); }
function uniqueSorted(field) { const set = new Set(rawData.map(x => clean(x[field])).filter(Boolean)); return Array.from(set).sort((a,b)=>a.localeCompare(b, 'ru')); }
function fillSelect(id, values) {
  const el = document.getElementById(id);
  el.innerHTML = '';
  const allOption = document.createElement('option');
  allOption.value = '';
  allOption.textContent = 'Все';
  el.appendChild(allOption);
  values.forEach(v => {
    const option = document.createElement('option');
    option.value = v;
    option.textContent = v;
    el.appendChild(option);
  });
}
function countBy(data, field, limit=10) { const map = new Map(); for (const item of data) { const key = clean(item[field]) || 'Не указано'; map.set(key, (map.get(key) || 0) + 1); } return Array.from(map.entries()).sort((a,b)=>b[1]-a[1]).slice(0, limit); }
function medianByYear(data) { const map = new Map(); for (const item of data) { const y = num(item.year), p = num(item.price); if (y !== null && p !== null && p > 0) { const key = String(Math.round(y)); if (!map.has(key)) map.set(key, []); map.get(key).push(p); } } return Array.from(map.entries()).map(([y, prices]) => [y, median(prices)]).sort((a,b)=>Number(a[0])-Number(b[0])); }
function makePriceHistogram(data, bins=24) { const prices = data.map(x => num(x.price)).filter(v => v !== null && v > 0).sort((a,b)=>a-b); if (prices.length < 2) return []; const min = prices[0], max = prices[prices.length - 1]; if (min === max) return [[priceText(min), prices.length]]; const safeBins = Math.max(6, Math.min(60, Math.round(bins || 24))); const step = (max - min) / safeBins; const result = Array.from({length: safeBins}, (_, i) => [fmt.format(Math.round(min + i * step)) + '–' + fmt.format(Math.round(min + (i + 1) * step)), 0]); for (const p of prices) { const idx = Math.min(safeBins - 1, Math.floor((p - min) / step)); result[idx][1] += 1; } return result; }
function filteredData() {
  const source = document.getElementById('sourceFilter').value;
  const brand = document.getElementById('brandFilter').value;
  const model = document.getElementById('modelFilter').value;
  const region = document.getElementById('regionFilter').value;
  const pMin = num(document.getElementById('priceMin').value), pMax = num(document.getElementById('priceMax').value);
  const yMin = num(document.getElementById('yearMin').value), yMax = num(document.getElementById('yearMax').value);
  const mMax = num(document.getElementById('mileageMax').value);
  const search = clean(document.getElementById('textSearch').value).toLowerCase();
  return rawData.filter(item => {
    const p = num(item.price), y = num(item.year), m = num(item.mileage);
    if (source && clean(item.source) !== source) return false;
    if (brand && clean(item.brand) !== brand) return false;
    if (model && clean(item.model) !== model) return false;
    if (region && clean(item.sale_region) !== region) return false;
    if (pMin !== null && (p === null || p < pMin)) return false;
    if (pMax !== null && (p === null || p > pMax)) return false;
    if (yMin !== null && (y === null || y < yMin)) return false;
    if (yMax !== null && (y === null || y > yMax)) return false;
    if (mMax !== null && (m === null || m > mMax)) return false;
    if (search) { const hay = [item.brand, item.model, item.sale_region, item.source, item.transmission, item.drive_type, item.fuel_type, item.body_type, item.description].map(clean).join(' ').toLowerCase(); if (!hay.includes(search)) return false; }
    return true;
  });
}
function metric(label, value, hint='') { return `<div class="metric"><div class="label">${escapeHtml(label)}</div><div class="value">${escapeHtml(value)}</div><div class="small">${escapeHtml(hint)}</div></div>`; }
function prepareCanvas(canvasId) { const canvas = document.getElementById(canvasId), ctx = canvas.getContext('2d'); const rect = canvas.getBoundingClientRect(); const ratio = window.devicePixelRatio || 1; const w = Math.max(320, rect.width || canvas.clientWidth || 640), h = Math.max(260, rect.height || canvas.clientHeight || 270); canvas.width = w * ratio; canvas.height = h * ratio; ctx.setTransform(ratio, 0, 0, ratio, 0, 0); ctx.clearRect(0,0,w,h); return [ctx, w, h]; }
function drawNoData(ctx, text) { ctx.fillStyle = '#667085'; ctx.font = '13px Arial'; ctx.fillText(text || 'Нет данных', 14, 26); }
function drawBar(canvasId, pairs) { const [ctx,w,h] = prepareCanvas(canvasId); if (!pairs.length) { drawNoData(ctx, 'Нет данных для графика'); return; } const left=145,right=18,top=14,rowH=Math.min(26,(h-28)/pairs.length); const max=Math.max(...pairs.map(x=>x[1]),1); ctx.font='12px Arial'; ctx.textBaseline='middle'; pairs.forEach((p,i)=>{ const y=top+i*rowH+rowH/2; const label=String(p[0]).length>22?String(p[0]).slice(0,21)+'…':String(p[0]); const bw=(w-left-right)*p[1]/max; ctx.fillStyle='#344054'; ctx.fillText(label,6,y); ctx.fillStyle='#1f6feb'; ctx.fillRect(left,y-rowH*.28,bw,rowH*.56); ctx.fillStyle='#667085'; ctx.fillText(fmt.format(Math.round(p[1])),left+bw+6,y); }); }
function drawLine(canvasId, pairs) { const [ctx,w,h] = prepareCanvas(canvasId); if (pairs.length < 2) { drawNoData(ctx, 'Недостаточно данных'); return; } const pad=48, vals=pairs.map(x=>x[1]); const min=Math.min(...vals), max=Math.max(...vals), span=max-min||1; ctx.strokeStyle='#d9e0e7'; ctx.beginPath(); ctx.moveTo(pad,pad); ctx.lineTo(pad,h-pad); ctx.lineTo(w-pad,h-pad); ctx.stroke(); ctx.strokeStyle='#1f6feb'; ctx.lineWidth=2; ctx.beginPath(); pairs.forEach((p,i)=>{ const x=pad+i*(w-pad*2)/(pairs.length-1); const y=h-pad-(p[1]-min)/span*(h-pad*2); if(i===0)ctx.moveTo(x,y); else ctx.lineTo(x,y); }); ctx.stroke(); ctx.fillStyle='#1f6feb'; pairs.forEach((p,i)=>{ const x=pad+i*(w-pad*2)/(pairs.length-1); const y=h-pad-(p[1]-min)/span*(h-pad*2); ctx.beginPath(); ctx.arc(x,y,3,0,Math.PI*2); ctx.fill(); }); ctx.fillStyle='#667085'; ctx.font='11px Arial'; ctx.fillText(pairs[0][0],pad,h-12); ctx.fillText(pairs[pairs.length-1][0],w-pad-44,h-12); ctx.fillText(priceText(max),pad+4,pad-12); ctx.fillText(priceText(min),pad+4,h-pad+14); }
function drawScatter(canvasId, data) { const [ctx,w,h] = prepareCanvas(canvasId); const pts=data.map(x=>[num(x.mileage),num(x.price)]).filter(p=>p[0]!==null&&p[1]!==null&&p[0]>0&&p[1]>0).slice(0,1500); if(pts.length<2){drawNoData(ctx, 'Недостаточно данных');return;} const padL=76,padR=20,padT=24,padB=48,xs=pts.map(p=>p[0]),ys=pts.map(p=>p[1]); const minX=Math.min(...xs),maxX=Math.max(...xs),minY=Math.min(...ys),maxY=Math.max(...ys); const sx=x=>padL+(x-minX)/(maxX-minX||1)*(w-padL-padR); const sy=y=>h-padB-(y-minY)/(maxY-minY||1)*(h-padT-padB); ctx.strokeStyle='#d9e0e7'; ctx.beginPath(); ctx.moveTo(padL,padT); ctx.lineTo(padL,h-padB); ctx.lineTo(w-padR,h-padB); ctx.stroke(); ctx.fillStyle='rgba(31,111,235,.65)'; pts.forEach(p=>{ ctx.beginPath(); ctx.arc(sx(p[0]),sy(p[1]),3,0,Math.PI*2); ctx.fill(); }); if(document.getElementById('trendToggle').checked && pts.length>=3){ const n=pts.length,sumX=pts.reduce((a,p)=>a+p[0],0),sumY=pts.reduce((a,p)=>a+p[1],0),sumXY=pts.reduce((a,p)=>a+p[0]*p[1],0),sumXX=pts.reduce((a,p)=>a+p[0]*p[0],0); const denom=n*sumXX-sumX*sumX; if(denom!==0){ const a=(n*sumXY-sumX*sumY)/denom,b=(sumY-a*sumX)/n; ctx.strokeStyle='#17212b'; ctx.lineWidth=2.2; ctx.beginPath(); ctx.moveTo(sx(minX),sy(a*minX+b)); ctx.lineTo(sx(maxX),sy(a*maxX+b)); ctx.stroke(); }} ctx.fillStyle='#667085'; ctx.font='11px Arial'; ctx.fillText('Пробег',w-82,h-12); ctx.fillText('Цена',8,22); ctx.fillText(fmt.format(Math.round(minX)),padL,h-28); ctx.fillText(fmt.format(Math.round(maxX)),w-padR-80,h-28); }
function renderTable(data) {
  const table=document.getElementById('recordsTable');
  const sortedData = sortRecords(data);
  const rows=sortedData.slice(0,120);
  const info = document.getElementById('tableInfo');
  if (!rawData.length) {
    info.textContent = 'В интерактивный отчёт не попало ни одной записи. Проверьте результат JSON анализатора: embeddedRecordsCount должен быть больше 0.';
  } else if (!data.length) {
    info.textContent = `После текущих фильтров нет объявлений. Всего в отчёте есть ${rawData.length} записей — попробуйте сбросить фильтры.`;
  } else {
    info.textContent = `Показано ${rows.length} из ${data.length} объявлений после фильтрации. Всего встроено ${rawData.length} записей.${sortDescription()}`;
  }
  const header = '<thead><tr>' + tableColumns.map(headerCell).join('') + '</tr></thead>';
  const body = '<tbody>' + rows.map(x=>`<tr><td>${escapeHtml(carName(x)||'—')}</td><td class="price">${priceText(x.price)}</td><td>${yearText(x.year)}</td><td>${numberText(x.mileage)}</td><td>${escapeHtml(x.body_type||'—')}</td><td>${escapeHtml(x.sale_region||'—')}</td><td>${escapeHtml(x.source||'—')}</td><td>${x.url?`<a target="_blank" href="${escapeHtml(x.url)}">Открыть</a>`:'—'}</td></tr>`).join('') + '</tbody>';
  table.innerHTML = header + body;
  bindTableSort();
}
function update() {
  try {
    const data=filteredData();
    renderTable(data);
    const prices=data.map(x=>num(x.price)).filter(v=>v!==null&&v>0);
    const years=data.map(x=>num(x.year)).filter(v=>v!==null);
    const mileages=data.map(x=>num(x.mileage)).filter(v=>v!==null&&v>0);
    document.getElementById('metrics').innerHTML=[metric('Объявлений',numberText(data.length),'После фильтров'),metric('Медианная цена',priceText(median(prices)),'По отфильтрованной выборке'),metric('Средняя цена',priceText(mean(prices)),'Чувствительна к выбросам'),metric('Медианный год',yearText(median(years)),''),metric('Медианный пробег',numberText(median(mileages)),'км'),metric('Источников',numberText(new Set(data.map(x=>clean(x.source)).filter(Boolean)).size),'')].join('');
    const bins=num(document.getElementById('histogramBins').value)||24;
    drawBar('priceHistogramCanvas',makePriceHistogram(data,bins));
    drawBar('brandChart',countBy(data,'brand',10));
    drawBar('regionChart',countBy(data,'sale_region',10));
    drawLine('priceByYearChart',medianByYear(data));
    drawScatter('scatterChart',data);
    drawBar('transmissionChart',countBy(data,'transmission',10));
  } catch (err) { showError(err); }
}
function init() {
  try {
    rawData = parseEmbeddedData();
    document.getElementById('diagnostics').textContent = `Загружено записей в отчёт: ${fmt.format(rawData.length)} из __TOTAL_COUNT__.`;
    fillSelect('sourceFilter',uniqueSorted('source'));
    fillSelect('brandFilter',uniqueSorted('brand'));
    fillSelect('modelFilter',uniqueSorted('model'));
    fillSelect('regionFilter',uniqueSorted('sale_region'));
    for (const id of ['sourceFilter','brandFilter','modelFilter','regionFilter','priceMin','priceMax','yearMin','yearMax','mileageMax','textSearch','histogramBins','trendToggle']) {
      const el=document.getElementById(id);
      el.addEventListener('input',update);
      el.addEventListener('change',update);
    }
    document.getElementById('resetButton').addEventListener('click',()=>{
      for (const id of ['sourceFilter','brandFilter','modelFilter','regionFilter','priceMin','priceMax','yearMin','yearMax','mileageMax','textSearch']) document.getElementById(id).value='';
      document.getElementById('histogramBins').value='24';
      document.getElementById('trendToggle').checked=true;
      sortState = { field: 'price', direction: 'asc' };
      update();
    });
    update();
  } catch (err) { showError(err); }
}
window.addEventListener('resize', update);
document.addEventListener('DOMContentLoaded', init);
</script>
</body>
</html>'''

    return (
        template
        .replace("__CREATED_AT__", created_at)
        .replace("__TRUNCATED_NOTE__", truncated_note)
        .replace("__EMBEDDED_COUNT__", embedded_count_text)
        .replace("__TOTAL_COUNT__", total_count_text)
        .replace("__RECORDS_JSON__", safe_json_for_script(records))
    )


def main():
    data_raw, meta = read_input_payload()
    df = dataframe_from_data(data_raw)
    file_path = build_report_path(__file__)
    records = prepare_dashboard_records(df)
    html_page = build_dashboard_html(records, len(df))
    finish_report(file_path, html_page, {
        "status": "ok",
        "file": file_path,
        "recordsCount": int(len(df)),
        "embeddedRecordsCount": int(len(records)),
        "reportType": "dynamic_dashboard",
    })


if __name__ == "__main__":
    main()
