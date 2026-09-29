const history = document.getElementById('history');
const form = document.getElementById('form');
const input = document.getElementById('input');
const send = form.querySelector('button');

// Full conversation as sent to the backend (which is stateless).
const messages = [];

function render(kind, text) {
  const el = document.createElement('div');
  el.className = `msg ${kind}`;
  el.textContent = text;
  history.appendChild(el);
  history.scrollTop = history.scrollHeight;
}

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  const text = input.value.trim();
  if (!text) return;

  input.value = '';
  messages.push({ role: 'user', text });
  render('user', text);
  send.disabled = true;

  try {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ messages }),
    });
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    const { reply } = await res.json();
    messages.push({ role: 'agent', text: reply });
    render('agent', reply);
  } catch (err) {
    render('error', err.message);
  } finally {
    send.disabled = false;
    input.focus();
  }
});
