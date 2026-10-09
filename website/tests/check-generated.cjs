'use strict';

// Check the generated, crawlable site rather than the templates that produce it.
// Intentionally uses only Node builtins so this can run directly after Hexo.
const fs = require('node:fs');
const path = require('node:path');

const website = path.resolve(__dirname, '..');
const publicDir = path.join(website, 'public');
const config = fs.readFileSync(path.join(website, '_config.yml'), 'utf8');
const scalar = value => String(value || '').trim().replace(/^(['"])(.*)\1$/, '$2');
const configValue = key => scalar(config.match(new RegExp(`^${key}:\\s*(.+)$`, 'm'))?.[1]);
const siteUrl = new URL(configValue('url'));
const timezone = configValue('timezone') || 'UTC';
const errors = [];
const counts = { pages: 0, references: 0, articles: 0, questions: 0, offers: 0 };
const check = (condition, message) => { if (!condition) errors.push(message); };

function filesBelow(directory) {
  if (!fs.existsSync(directory)) return [];
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(directory, entry.name);
    return entry.isDirectory() ? filesBelow(file) : [file];
  });
}

function entities(value) {
  const names = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ', ndash: '–', mdash: '—', hellip: '…', copy: '©' };
  return String(value).replace(/&(#x[\da-f]+|#\d+|[a-z]+);/gi, (whole, entity) => {
    if (entity[0] !== '#') return names[entity.toLowerCase()] ?? whole;
    const number = entity[1].toLowerCase() === 'x' ? parseInt(entity.slice(2), 16) : parseInt(entity.slice(1), 10);
    return number > 0 && number <= 0x10ffff ? String.fromCodePoint(number) : whole;
  });
}

const compact = value => entities(value).replace(/\s+/g, '');
const normalizedUrl = value => new URL(entities(value), siteUrl).href;
const hasType = (node, type) => [].concat(node?.['@type'] || []).includes(type);

function attributes(source) {
  const result = {};
  const pattern = /([^\s=/>]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+)))?/g;
  for (const match of source.matchAll(pattern)) result[match[1].toLowerCase()] = entities(match[2] ?? match[3] ?? match[4] ?? '');
  return result;
}

// A small HTML reader for generated pages, with raw-text and hidden subtree handling.
// It does not execute scripts or try to infer visibility from runtime CSS classes.
function readHtml(html) {
  const root = { tag: '#document', attrs: {}, children: [] };
  const stack = [root];
  const nodes = [];
  const voidTags = new Set('area base br col embed hr img input link meta param source track wbr'.split(' '));
  const tokens = /<!--[\s\S]*?-->|<![^>]*>|<\/?([a-z][\w:-]*)\b(?:[^>"']|"[^"]*"|'[^']*')*>|[^<]+|</gi;
  let match;
  while ((match = tokens.exec(html))) {
    const token = match[0];
    if (token.startsWith('<!')) continue;
    if (!match[1]) {
      stack.at(-1).children.push(entities(token));
      continue;
    }
    const tag = match[1].toLowerCase();
    if (token.startsWith('</')) {
      const position = stack.findLastIndex(node => node.tag === tag);
      if (position > 0) stack.length = position;
      continue;
    }
    const node = { tag, attrs: attributes(token.slice(match[1].length + 1, -1)), children: [], parent: stack.at(-1) };
    stack.at(-1).children.push(node);
    nodes.push(node);
    if (['script', 'style', 'textarea', 'title'].includes(tag)) {
      const end = new RegExp(`</${tag}\\s*>`, 'gi');
      end.lastIndex = tokens.lastIndex;
      const closing = end.exec(html);
      node.children.push(html.slice(tokens.lastIndex, closing ? closing.index : html.length));
      tokens.lastIndex = closing ? end.lastIndex : html.length;
    } else if (!voidTags.has(tag) && !token.endsWith('/>')) stack.push(node);
  }
  return { root, nodes };
}

function hidden(node) {
  return ['head', 'script', 'style', 'template'].includes(node.tag)
    || Object.hasOwn(node.attrs, 'hidden')
    || node.attrs['aria-hidden'] === 'true'
    || /(?:display\s*:\s*none|visibility\s*:\s*hidden)/i.test(node.attrs.style || '');
}

function textOf(node, visible = false) {
  if (typeof node === 'string') return node;
  if (visible && hidden(node)) return '';
  return node.children.map(child => textOf(child, visible)).join(' ');
}

function isVisible(node) {
  for (let current = node; current; current = current.parent) if (hidden(current)) return false;
  return true;
}

function walkJson(value, nodes = []) {
  if (Array.isArray(value)) value.forEach(item => walkJson(item, nodes));
  else if (value && typeof value === 'object') {
    nodes.push(value);
    Object.values(value).forEach(item => walkJson(item, nodes));
  }
  return nodes;
}

function targetFile(url) {
  const pathname = decodeURIComponent(url.pathname);
  let file = path.resolve(publicDir, '.' + pathname);
  if (!file.startsWith(publicDir + path.sep) && file !== publicDir) return undefined;
  if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
  return fs.existsSync(file) && fs.statSync(file).isFile() ? file : undefined;
}

const documents = new Map();
const titles = new Map();
for (const file of filesBelow(publicDir).filter(file => /\.html$/i.test(file))) {
  const label = path.relative(publicDir, file).replaceAll(path.sep, '/');
  const parsed = readHtml(fs.readFileSync(file, 'utf8'));
  const select = (tag, predicate = () => true) => parsed.nodes.filter(node => node.tag === tag && predicate(node));
  const canonicalLinks = select('link', node => (node.attrs.rel || '').toLowerCase().split(/\s+/).includes('canonical'));
  const descriptions = select('meta', node => node.attrs.name?.toLowerCase() === 'description');
  const pageTitles = select('title');
  const headings = select('h1');
  const ogUrls = select('meta', node => node.attrs.property?.toLowerCase() === 'og:url');
  check(canonicalLinks.length === 1, `${label}: expected exactly one canonical link`);
  check(descriptions.length === 1 && descriptions[0].attrs.content?.trim(), `${label}: expected one nonempty meta description`);
  check(headings.length === 1 && compact(textOf(headings[0], true)), `${label}: expected one nonempty H1`);
  check(pageTitles.length === 1 && compact(textOf(pageTitles[0])), `${label}: expected one nonempty title`);
  const title = pageTitles[0] && compact(textOf(pageTitles[0]));
  if (title) {
    check(!titles.has(title), `${label}: title duplicates ${titles.get(title)}`);
    titles.set(title, label);
  }
  let canonical;
  try {
    const value = canonicalLinks[0]?.attrs.href;
    check(/^https?:\/\//i.test(value || ''), `${label}: canonical must be an absolute HTTP URL`);
    canonical = normalizedUrl(value || '/');
    const url = new URL(canonical);
    check(url.origin === siteUrl.origin && !url.search && !url.hash, `${label}: canonical must use the official origin without query or hash`);
    check(targetFile(url) === file, `${label}: canonical does not point to its own generated page`);
    check(!/\/index\.html$/.test(url.pathname), `${label}: canonical should omit index.html`);
    check(ogUrls.length === 1 && normalizedUrl(ogUrls[0].attrs.content) === canonical, `${label}: og:url must equal canonical`);
  } catch (error) { errors.push(`${label}: invalid canonical / og:url (${error.message})`); }
  const jsonNodes = [];
  const jsonScripts = select('script', node => node.attrs.type?.toLowerCase() === 'application/ld+json');
  check(jsonScripts.length > 0, `${label}: missing JSON-LD`);
  for (const script of jsonScripts) {
    try { walkJson(JSON.parse(script.children.join('')), jsonNodes); }
    catch (error) { errors.push(`${label}: invalid JSON-LD (${error.message})`); }
  }
  const webPages = jsonNodes.filter(node => ['WebPage', 'CollectionPage', 'FAQPage'].some(type => hasType(node, type)));
  check(webPages.length === 1, `${label}: expected one WebPage or WebPage subtype in JSON-LD`);
  for (const webpage of webPages) {
    try { check(normalizedUrl(webpage.url) === canonical, `${label}: WebPage URL must equal canonical`); }
    catch { errors.push(`${label}: invalid WebPage URL`); }
  }
  const ids = new Map(parsed.nodes.filter(node => node.attrs.id).map(node => [node.attrs.id, node]));
  const noindex = select('meta', node => node.attrs.name?.toLowerCase() === 'robots').some(node => /(?:^|[\s,])noindex(?:$|[\s,])/i.test(node.attrs.content || ''));
  documents.set(file, { ...parsed, label, canonical, jsonNodes, ids, visible: compact(textOf(parsed.root, true)), indexable: !noindex && !/(?:^|\/)404\.html$/.test(label) });
}
counts.pages = documents.size;
check(counts.pages > 0, 'No generated HTML found; run the Hexo build first.');

for (const document of documents.values()) {
  for (const node of document.nodes) {
    for (const attribute of ['href', 'src']) {
      if (!Object.hasOwn(node.attrs, attribute)) continue;
      const reference = node.attrs[attribute];
      try {
        const url = new URL(reference, document.canonical || siteUrl);
        if (!['http:', 'https:'].includes(url.protocol) || url.origin !== siteUrl.origin) continue;
        counts.references++;
        const file = targetFile(url);
        check(file, `${document.label}: missing internal ${attribute} target ${reference}`);
        const fragment = decodeURIComponent(url.hash.slice(1)).split(':~:')[0];
        if (file && fragment) check(documents.get(file)?.ids.has(fragment), `${document.label}: missing fragment ${reference}`);
      } catch (error) { errors.push(`${document.label}: invalid ${attribute} ${reference} (${error.message})`); }
    }
  }
}

function generatedText(name) {
  const file = path.join(publicDir, name);
  check(fs.existsSync(file), `Missing generated ${name}`);
  return fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : '';
}

const sitemap = generatedText('sitemap.xml');
const sitemapUrls = [];
for (const match of sitemap.matchAll(/<loc\b[^>]*>([\s\S]*?)<\/loc>/gi)) {
  try { sitemapUrls.push(normalizedUrl(match[1].trim())); }
  catch { errors.push(`sitemap.xml: invalid URL ${match[1]}`); }
}
const indexable = new Set([...documents.values()].filter(document => document.indexable).map(document => document.canonical));
const sitemapSet = new Set(sitemapUrls);
check(sitemapSet.size === sitemapUrls.length, 'sitemap.xml: duplicate URLs');
for (const url of indexable) check(sitemapSet.has(url), `sitemap.xml: missing indexable page ${url}`);
for (const url of sitemapSet) check(indexable.has(url), `sitemap.xml: unexpected or nonindexable URL ${url}`);

const llms = generatedText('llms.txt');
const fullText = generatedText('llms-full.txt');
const llmsLinks = new Set();
for (const match of llms.matchAll(/\]\((https?:\/\/[^\s)]+)\)/g)) {
  try { llmsLinks.add(normalizedUrl(match[1])); }
  catch { errors.push(`llms.txt: invalid URL ${match[1]}`); }
}
const corePaths = ['/download/', '/activate/', '/faq/', '/resources/', '/about/', '/archives/'];
const productData = fs.readFileSync(path.join(website, 'source/_data/product.yml'), 'utf8');
const configuredPaths = [...productData.matchAll(/^\s+path:\s*(.+)$/gm)].map(match => scalar(match[1]));
for (const route of new Set([...corePaths, ...configuredPaths])) check(llmsLinks.has(normalizedUrl(route)), `llms.txt: missing official page ${route}`);

function plainMarkdown(value) {
  return compact(value.replace(/<!--[\s\S]*?-->/g, '')
    .replace(/<[^>]+>/g, ' ')
    .replace(/!?\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/[*_`~]/g, ''));
}
const fullPlain = plainMarkdown(fullText);
const dateFormat = new Intl.DateTimeFormat('en-GB', { timeZone: timezone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' });
const authoredPosts = filesBelow(path.join(website, 'source/_posts')).filter(file => /\.md$/i.test(file)).map(file => {
  const source = fs.readFileSync(file, 'utf8');
  const front = source.match(/^---\r?\n([\s\S]*?)\r?\n---/)?.[1] || '';
  const value = key => scalar(front.match(new RegExp(`^${key}:\\s*(.+)$`, 'm'))?.[1]);
  return { title: value('title'), date: value('date'), updated: value('updated'), author: value('author') };
});
const renderedArticles = [];

function checkAuthoredDate(document, value, authored, field) {
  const parsed = new Date(value);
  const valid = typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value) && !Number.isNaN(parsed.valueOf());
  check(valid, `${document.label}: ${field} must be an ISO date`);
  if (!valid) return;
  const parts = Object.fromEntries(dateFormat.formatToParts(parsed).map(part => [part.type, part.value]));
  const localDate = `${parts.year}-${parts.month}-${parts.day}`;
  const expected = authored?.match(/^(\d{4}-\d{2}-\d{2})(?:[ T](\d{2}:\d{2}:\d{2}))?/);
  check(expected && localDate === expected[1], `${document.label}: ${field} does not match its authored date`);
  if (expected?.[2]) check(`${parts.hour}:${parts.minute}:${parts.second}` === expected[2], `${document.label}: ${field} does not match its authored time`);
  const timeNodes = document.nodes.filter(node => node.tag === 'time' && isVisible(node) && new Date(node.attrs.datetime).valueOf() === parsed.valueOf());
  check(timeNodes.some(node => {
    const numbers = textOf(node, true).match(/\d+/g) || [];
    return numbers.length >= 3 && Number(numbers[0]) === Number(parts.year) && Number(numbers[1]) === Number(parts.month) && Number(numbers[2]) === Number(parts.day);
  }), `${document.label}: ${field} needs a visible time element with the matching date`);
}

for (const document of documents.values()) {
  for (const article of document.jsonNodes.filter(node => hasType(node, 'BlogPosting'))) {
    counts.articles++;
    renderedArticles.push(article.headline);
    const authored = authoredPosts.find(post => post.title === article.headline);
    check(authored, `${document.label}: article has no matching source post`);
    if (authored) {
      checkAuthoredDate(document, article.datePublished, authored.date, 'datePublished');
      checkAuthoredDate(document, article.dateModified, authored.updated || authored.date, 'dateModified');
      if (!authored.updated) check(article.dateModified === article.datePublished, `${document.label}: modified date must fall back to the published date, not filesystem mtime`);
      check(authored.author && document.visible.includes(compact(authored.author)), `${document.label}: authored name must be visible`);
    }
    check(llmsLinks.has(document.canonical), `llms.txt: missing article ${document.canonical}`);
    check(fullText.includes(document.canonical), `llms-full.txt: missing article source ${document.canonical}`);
    const content = document.nodes.find(node => (node.attrs.class || '').split(/\s+/).includes('article-content'));
    const paragraphs = document.nodes.filter(node => {
      if (node.tag !== 'p' || compact(textOf(node, true)).length < 20) return false;
      for (let parent = node.parent; parent; parent = parent.parent) if (parent === content) return true;
      return false;
    });
    check(paragraphs.length > 0, `${document.label}: missing readable article paragraphs`);
    for (const paragraph of paragraphs) check(fullPlain.includes(compact(textOf(paragraph, true))), `llms-full.txt: missing article text from ${document.label}: ${textOf(paragraph, true).trim().slice(0, 42)}`);
  }
  for (const faq of document.jsonNodes.filter(node => hasType(node, 'FAQPage'))) {
    const questions = [].concat(faq.mainEntity || []);
    check(questions.length > 0, `${document.label}: FAQPage has no questions`);
    for (const question of questions) {
      counts.questions++;
      check(hasType(question, 'Question'), `${document.label}: FAQ mainEntity must contain Questions`);
      check(typeof question.name === 'string' && document.visible.includes(compact(question.name)), `${document.label}: FAQ question is not visible: ${question.name}`);
      const answers = [].concat(question.acceptedAnswer || []);
      check(answers.length > 0, `${document.label}: FAQ question has no accepted answer`);
      for (const answer of answers) check(hasType(answer, 'Answer') && typeof answer.text === 'string' && document.visible.includes(compact(answer.text)), `${document.label}: FAQ answer is not visible: ${question.name}`);
      if (question['@id']) {
        try {
          const fragment = decodeURIComponent(new URL(question['@id']).hash.slice(1));
          const section = document.ids.get(fragment);
          check(section && isVisible(section), `${document.label}: FAQ question needs a visible matching id: ${fragment}`);
        } catch { errors.push(`${document.label}: invalid FAQ question @id`); }
      }
    }
  }
  for (const software of document.jsonNodes.filter(node => hasType(node, 'SoftwareApplication'))) {
    const offers = [].concat(software.offers || []);
    const offeredPaths = new Set();
    const route = document.canonical && new URL(document.canonical).pathname;
    if (['/', '/download/', '/activate/', '/faq/'].includes(route)) check(offers.length === 2, `${document.label}: expected separate free and activation offers`);
    for (const offer of offers) {
      counts.offers++;
      try {
        const url = new URL(offer.url);
        offeredPaths.add(url.pathname);
        const expected = url.pathname === '/download/' ? 0 : url.pathname === '/activate/' ? 6.88 : undefined;
        check(hasType(offer, 'Offer') && url.origin === siteUrl.origin && expected !== undefined && /^\d+(?:\.\d+)?$/.test(String(offer.price)) && Number(offer.price) === expected && offer.priceCurrency === 'CNY', `${document.label}: offer price/currency must be 0 CNY for download or 6.88 CNY for activation`);
        const target = documents.get(targetFile(url));
        check(target && (expected === 0 ? target.visible.includes('基础陪伴免费') : target.visible.includes('6.88') && target.visible.includes('永久激活')), `${document.label}: offer needs matching visible pricing on its linked page`);
      } catch { errors.push(`${document.label}: invalid offer URL`); }
    }
    if (offers.length) check(offeredPaths.has('/download/') && offeredPaths.has('/activate/'), `${document.label}: offers must cover both free download and permanent activation`);
  }
}
check(counts.articles === authoredPosts.length && counts.articles > 0, `Expected ${authoredPosts.length} generated articles; found ${counts.articles}`);
for (const authored of authoredPosts) check(renderedArticles.filter(title => title === authored.title).length === 1, `Expected exactly one generated article for ${authored.title}`);
check(counts.questions > 0, 'No visible, structured FAQ questions found');
check(counts.offers > 0, 'No structured product offers found');

const summary = `${counts.pages} HTML pages, ${sitemapSet.size} sitemap URLs, ${counts.articles} articles, ${counts.questions} FAQ answers, ${counts.references} internal references, ${counts.offers} offers`;
if (errors.length) {
  console.error(`GEO validation failed: ${errors.length} issue(s); ${summary}.`);
  errors.slice(0, 30).forEach(error => console.error(`- ${error}`));
  if (errors.length > 30) console.error(`- ${errors.length - 30} additional issue(s) omitted.`);
  process.exitCode = 1;
} else console.log(`GEO validation passed: ${summary}.`);
