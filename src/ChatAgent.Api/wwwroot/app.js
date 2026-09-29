const history = document.getElementById('history');
const form = document.getElementById('form');
const input = document.getElementById('input');
const send = document.getElementById('send');
const reset = document.getElementById('reset');
const languageSelect = document.getElementById('language');

const REQUEST_TIMEOUT_MS = 90_000; // the server may retry Gemini a few times

// The backend is stateless: we send the whole conversation plus the last label specification.
let messages = [];
let label = null;
let inFlight = null; // AbortController of the running request, aborted by "New chat"

function bubble(kind, text, i18nKey) {
  const node = document.createElement('div');
  node.className = `msg ${kind}`;
  node.textContent = text;
  if (i18nKey) node.dataset.i18n = i18nKey; // re-translated when the language changes
  history.appendChild(node);
  scrollDown();
  return node;
}

function scrollDown() {
  history.scrollTop = history.scrollHeight;
}

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text) node.textContent = text;
  return node;
}

/** An element whose text is translated now and again whenever the language changes. */
function tr(tag, className, key, args) {
  const node = el(tag, className);
  node.dataset.i18n = key;
  if (args) node.dataset.i18nArgs = JSON.stringify(args);
  node.textContent = t(key, args);
  return node;
}

function labelDetails(spec) {
  const rows = [
    ['field.type', spec.packagingLevel && `level.${spec.packagingLevel}`, true],
    ['field.gtin', spec.gtin],
    ['field.sscc', spec.sscc],
    ['field.batch', spec.batch],
    ['field.bestBefore', spec.bestBefore],
    ['field.items', spec.itemCount],
    ['field.barcode', spec.symbology],
  ].filter(([, value]) => value);
  const list = el('dl', 'details');
  for (const [labelKey, value, translated] of rows) {
    list.append(tr('dt', '', labelKey), translated ? tr('dd', '', value) : el('dd', '', String(value)));
  }
  return list;
}

/** The printable label: product text, barcode at its physical size, key facts. */
function buildLabel(spec, imageUrl, dpi) {
  const card = el('div', 'label');
  card.append(el('h3', '', spec.productName || 'Label'));
  if (spec.netVolume) card.append(el('p', 'volume', spec.netVolume));

  const img = el('img');
  img.dataset.i18nAlt = 'barcodeAlt';
  img.alt = t('barcodeAlt');
  img.addEventListener('load', () => {
    img.style.width = `${(img.naturalWidth / dpi) * 25.4}mm`; // true size when printed
  });
  img.src = imageUrl;
  card.append(img, labelDetails(spec));
  return card;
}

function showLabel(bubbleEl, data) {
  const spec = data.label;
  const card = buildLabel(spec, data.image, data.dpi);

  const download = tr('a', '', 'downloadPng');
  download.href = data.image;
  download.download = `${(spec.productName || 'label').replace(/\W+/g, '-').toLowerCase()}-barcode.png`;

  const print = tr('button', 'ghost', 'printLabel');
  print.type = 'button';
  print.addEventListener('click', () => printLabel(card));

  const actions = el('div', 'actions');
  actions.append(print, download);
  bubbleEl.append(card);
  for (const notice of data.notices || []) {
    bubbleEl.append(tr('p', 'note', `notice.${notice}`, { gtin: spec.gtin }));
  }
  bubbleEl.append(actions);
  scrollDown();
}

/** Prints only the label: wait until the image is decoded, keep it in the DOM until printing is done. */
async function printLabel(card) {
  const area = document.getElementById('print-area');
  const copy = card.cloneNode(true);
  area.replaceChildren(copy);
  await Promise.all([...copy.querySelectorAll('img')].map((img) => img.decode().catch(() => {})));
  window.addEventListener('afterprint', () => area.replaceChildren(), { once: true });
  window.print();
}

function start() {
  inFlight?.abort();
  messages = [];
  label = null;
  history.replaceChildren();
  send.disabled = false;
  bubble('agent', t('greeting'), 'greeting');
  input.focus();
}

function errorText(err, status) {
  if (err.name === 'TimeoutError') return t('error.timeout');
  if (err instanceof TypeError) return t('error.unreachable');
  return err.message;
}

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  const text = input.value.trim();
  if (!text || send.disabled) return;

  input.value = '';
  messages.push({ role: 'user', text });
  const userBubble = bubble('user', text);
  send.disabled = true;
  const pending = bubble('agent pending', '…');

  const controller = (inFlight = new AbortController());
  try {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ messages, label }),
      signal: AbortSignal.any([controller.signal, AbortSignal.timeout(REQUEST_TIMEOUT_MS)]),
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || t('error.failed', { status: res.status }));

    messages.push({ role: 'agent', text: data.reply });
    label = data.label;
    pending.className = 'msg agent';
    pending.textContent = data.reply;
    if (data.image) showLabel(pending, data);
  } catch (err) {
    if (controller.signal.aborted) return; // "New chat" replaced the conversation; drop the late result
    messages.pop();
    userBubble.remove();
    input.value = text; // let the user retry without retyping
    pending.className = 'msg error';
    pending.textContent = errorText(err);
  } finally {
    if (inFlight === controller) {
      inFlight = null;
      send.disabled = false;
      input.focus();
      scrollDown();
    }
  }
});

reset.addEventListener('click', start);
languageSelect.addEventListener('change', () => setLanguage(languageSelect.value));
languageSelect.value = language;
applyLanguage();
start();
