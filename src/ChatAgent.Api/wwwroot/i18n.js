// UI texts. Chat replies come from the model in the user's own language; this only covers the interface.
const TEXTS = {
  en: {
    subtitle: 'beverage labels',
    newChat: 'New chat',
    placeholder: 'Describe your product…',
    send: 'Send',
    language: 'Language',
    greeting:
      'Hi! Describe the product and packaging you need a label for, e.g. ' +
      '"0.5 l apple juice bottle, GTIN 4006381333931". I will ask for anything that is missing.',
    printLabel: 'Print label',
    downloadPng: 'Download PNG',
    barcodeAlt: 'Barcode',
    'field.type': 'Type',
    'field.gtin': 'GTIN',
    'field.sscc': 'SSCC',
    'field.batch': 'Batch',
    'field.bestBefore': 'Best before',
    'field.items': 'Items',
    'field.barcode': 'Barcode',
    'level.consumer_unit': 'Consumer unit',
    'level.case': 'Case',
    'level.pallet': 'Pallet',
    'notice.gtin_completed': 'GTIN completed with check digit: {gtin}',
    'error.timeout': 'The request took too long. Please try again.',
    'error.unreachable': 'The server is not reachable.',
    'error.failed': 'Request failed ({status})',
  },
  de: {
    subtitle: 'Getränkeetiketten',
    newChat: 'Neuer Chat',
    placeholder: 'Beschreibe dein Produkt…',
    send: 'Senden',
    language: 'Sprache',
    greeting:
      'Hallo! Beschreibe das Produkt und die Verpackung, für die du ein Etikett brauchst, z. B. ' +
      '"0,5 l Apfelsaft Flasche, GTIN 4006381333931". Ich frage nach, was noch fehlt.',
    printLabel: 'Etikett drucken',
    downloadPng: 'PNG herunterladen',
    barcodeAlt: 'Barcode',
    'field.type': 'Typ',
    'field.gtin': 'GTIN',
    'field.sscc': 'SSCC',
    'field.batch': 'Charge',
    'field.bestBefore': 'Mindestens haltbar bis',
    'field.items': 'Stück',
    'field.barcode': 'Barcode',
    'level.consumer_unit': 'Verbrauchereinheit',
    'level.case': 'Karton',
    'level.pallet': 'Palette',
    'notice.gtin_completed': 'GTIN um Prüfziffer ergänzt: {gtin}',
    'error.timeout': 'Die Anfrage hat zu lange gedauert. Bitte versuche es erneut.',
    'error.unreachable': 'Der Server ist nicht erreichbar.',
    'error.failed': 'Anfrage fehlgeschlagen ({status})',
  },
};

const STORAGE_KEY = 'label-agent-language';
let language = initialLanguage();

function initialLanguage() {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored in TEXTS) return stored;
  } catch { /* storage unavailable (private mode): fall through */ }
  return navigator.language?.toLowerCase().startsWith('de') ? 'de' : 'en';
}

/** Translates a key; `{name}` placeholders are filled from `args`. Unknown keys are shown as-is. */
function t(key, args = {}) {
  const text = TEXTS[language][key] ?? TEXTS.en[key] ?? key;
  return text.replace(/\{(\w+)\}/g, (_, name) => args[name] ?? '');
}

/** Re-translates everything marked with data-i18n (text), data-i18n-placeholder, data-i18n-alt; args in data-i18n-args (JSON). */
function applyLanguage(root = document) {
  document.documentElement.lang = language;
  const args = (node) => JSON.parse(node.dataset.i18nArgs || '{}');
  root.querySelectorAll('[data-i18n]').forEach((node) => (node.textContent = t(node.dataset.i18n, args(node))));
  root.querySelectorAll('[data-i18n-placeholder]').forEach((node) => (node.placeholder = t(node.dataset.i18nPlaceholder)));
  root.querySelectorAll('[data-i18n-alt]').forEach((node) => (node.alt = t(node.dataset.i18nAlt)));
  root.querySelectorAll('[data-i18n-aria]').forEach((node) => node.setAttribute('aria-label', t(node.dataset.i18nAria)));
}

function setLanguage(next) {
  if (!(next in TEXTS)) return;
  language = next;
  try { localStorage.setItem(STORAGE_KEY, next); } catch { /* not persisted, still applied */ }
  applyLanguage();
}
