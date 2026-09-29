const history = document.getElementById('history');
const form = document.getElementById('form');
const input = document.getElementById('input');
const send = document.getElementById('send');
const reset = document.getElementById('reset');

const GREETING =
  'Hi! Describe the product and packaging you need a label for, e.g. ' +
  '"0.5 l apple juice bottle, GTIN 4006381333931". I will ask for anything that is missing.';
const REQUEST_TIMEOUT_MS = 90_000; // the server may retry Gemini a few times

// The backend is stateless: we send the whole conversation plus the last label specification.
let messages = [];
let label = null;
let inFlight = null; // AbortController of the running request, aborted by "New chat"

function bubble(kind, text) {
  const node = document.createElement('div');
  node.className = `msg ${kind}`;
  node.textContent = text;
  history.appendChild(node);
  scrollDown();
  return node;
}

function scrollDown() {
  history.scrollTop = history.scrollHeight;
}

const LEVELS = { consumer_unit: 'Consumer unit', case: 'Case', pallet: 'Pallet' };

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text) node.textContent = text;
  return node;
}

function labelDetails(spec) {
  const rows = [
    ['Type', LEVELS[spec.packagingLevel] || spec.packagingLevel],
    ['GTIN', spec.gtin],
    ['SSCC', spec.sscc],
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
function buildLabel(spec, imageUrl, dpi) {
  const card = el('div', 'label');
  card.append(el('h3', '', spec.productName || 'Label'));
  if (spec.netVolume) card.append(el('p', 'volume', spec.netVolume));

  const img = el('img');
  img.alt = `Barcode ${spec.symbology || ''}`;
  img.addEventListener('load', () => {
    img.style.width = `${(img.naturalWidth / dpi) * 25.4}mm`; // true size when printed
  });
  img.src = imageUrl;
  card.append(img, labelDetails(spec));
  return card;
}

function showLabel(bubbleEl, imageUrl, spec, dpi) {
  const card = buildLabel(spec, imageUrl, dpi);

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
  bubble('agent', GREETING);
  input.focus();
}

function errorText(err) {
  if (err.name === 'TimeoutError') return 'The request took too long. Please try again.';
  if (err instanceof TypeError) return 'The server is not reachable.';
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
    if (!res.ok) throw new Error(data.error || `Request failed (${res.status})`);

    messages.push({ role: 'agent', text: data.reply });
    label = data.label;
    pending.className = 'msg agent';
    pending.textContent = data.reply;
    if (data.image) showLabel(pending, data.image, data.label, data.dpi);
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
start();
