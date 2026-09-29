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

function showLabel(bubbleEl, imageUrl, name) {
  const card = document.createElement('figure');
  card.className = 'label-card';

  const img = document.createElement('img');
  img.src = imageUrl;
  img.alt = `Generated barcode for ${name || 'label'}`;

  const link = document.createElement('a');
  link.href = imageUrl;
  link.download = `${(name || 'label').replace(/\W+/g, '-').toLowerCase()}.png`;
  link.textContent = 'Download PNG';

  card.append(img, link);
  bubbleEl.appendChild(card);
  scrollDown();
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
    if (data.image) showLabel(pending, data.image, data.label?.productName);
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
