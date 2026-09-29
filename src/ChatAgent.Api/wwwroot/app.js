const history = document.getElementById('history');
const form = document.getElementById('form');
const input = document.getElementById('input');
const send = document.getElementById('send');
const reset = document.getElementById('reset');

const GREETING =
  'Hi! Describe the product and packaging you need a label for, e.g. ' +
  '"0.5 l apple juice bottle, GTIN 4006381333931". I will ask for anything that is missing.';

// The backend is stateless: we send the whole conversation plus the last label specification.
let messages = [];
let label = null;

function bubble(kind, text) {
  const el = document.createElement('div');
  el.className = `msg ${kind}`;
  el.textContent = text;
  history.appendChild(el);
  scrollDown();
  return el;
}

function scrollDown() {
  history.scrollTop = history.scrollHeight;
}

const LEVELS = { consumer_unit: 'Consumer unit', case: 'Case', pallet: 'Pallet' };
const DPI = 300; // must match LabelValidator.Dpi

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text) node.textContent = text;
  return node;
}

function labelDetails(spec) {
  const rows = [
    ['Type', LEVELS[spec.packagingLevel] || spec.packagingLevel],
    ['Batch', spec.batch],
    ['Best before', spec.bestBefore],
    ['Items', spec.itemCount],
    ['Barcode', spec.symbology],
  ].filter(([, value]) => value);
  const list = el('dl', 'details');
  for (const [name, value] of rows) list.append(el('dt', '', name), el('dd', '', String(value)));
  return list;
}

/** The printable label: product text, barcode at its physical size, key facts. */
function buildLabel(spec, imageUrl) {
  const card = el('div', 'label');
  card.append(el('h3', '', spec.productName || 'Label'));
  if (spec.netVolume) card.append(el('p', 'volume', spec.netVolume));

  const img = el('img');
  img.alt = `Barcode ${spec.symbology || ''}`;
  img.addEventListener('load', () => {
    img.style.width = `${(img.naturalWidth / DPI) * 25.4}mm`; // true size when printed
  });
  img.src = imageUrl;
  card.append(img, labelDetails(spec));
  return card;
}

function showLabel(bubbleEl, imageUrl, spec) {
  const card = buildLabel(spec, imageUrl);

  const download = el('a', '', 'Download PNG');
  download.href = imageUrl;
  download.download = `${(spec.productName || 'label').replace(/\W+/g, '-').toLowerCase()}-barcode.png`;

  const print = el('button', 'ghost', 'Print label');
  print.type = 'button';
  print.addEventListener('click', () => printLabel(card));

  const actions = el('div', 'actions');
  actions.append(print, download);
  bubbleEl.append(card, actions);
  scrollDown();
}

function printLabel(card) {
  const area = document.getElementById('print-area');
  area.replaceChildren(card.cloneNode(true));
  window.print();
  area.replaceChildren();
}

function start() {
  messages = [];
  label = null;
  history.replaceChildren();
  bubble('agent', GREETING);
  input.focus();
}

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  const text = input.value.trim();
  if (!text || send.disabled) return;

  input.value = '';
  messages.push({ role: 'user', text });
  bubble('user', text);
  send.disabled = true;
  const pending = bubble('agent pending', '…');

  try {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ messages, label }),
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || `Request failed (${res.status})`);

    messages.push({ role: 'agent', text: data.reply });
    label = data.label;
    pending.className = 'msg agent';
    pending.textContent = data.reply;
    if (data.image) showLabel(pending, data.image, data.label);
  } catch (err) {
    messages.pop(); // let the user resend
    pending.className = 'msg error';
    pending.textContent = err.message;
  } finally {
    send.disabled = false;
    input.focus();
    scrollDown();
  }
});

reset.addEventListener('click', start);
start();
