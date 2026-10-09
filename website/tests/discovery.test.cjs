'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const {
  absoluteUrl,
  editorialDate,
  pageInfo,
  structuredData,
  serializeJsonLd
} = require('../lib/discovery.cjs');

const config = {
  title: '桌搭子',
  description: '陪伴、互动和小剧场。',
  url: 'https://desktoppet.online'
};

const product = {
  name: '桌搭子',
  alternate_name: 'DeskPet',
  description: '支持 Windows、macOS 和 Android 的桌面宠物。',
  platforms: ['Windows', 'macOS', 'Android'],
  features: ['基础陪伴', '双宠小剧场'],
  free: {
    name: '基础陪伴与首次体验',
    price: '0',
    description: '基础陪伴免费，首次提供 7 天进阶体验。'
  },
  activation: {
    name: '永久激活',
    price: '6.88',
    currency: 'CNY',
    description: '一次购买永久激活；同一账号最多授权两台设备。'
  }
};

const questions = [
  { id: 'price', question: '激活需要多少钱？', answer: '6.88 元，一次购买永久激活。' },
  { id: 'devices', question: '能用几台设备？', answer: '同一账号最多授权两台设备。' }
];

function dataFor(page, faq = []) {
  const info = pageInfo(config, page);
  return structuredData(config, product, faq, page, info, info.description,
    absoluteUrl(config, 'images/promo/hello.webp'));
}

function entity(data, type) {
  const result = data['@graph'].find(item => item['@type'] === type);
  assert.ok(result, `Expected a ${type} entity`);
  return result;
}

function assertResolvedReferences(data) {
  const ids = new Set();
  const references = [];
  function visit(value) {
    if (!value || typeof value !== 'object') return;
    if (value['@id']) {
      if (Object.keys(value).length === 1) references.push(value['@id']);
      else ids.add(value['@id']);
    }
    for (const child of Object.values(value)) visit(child);
  }
  visit(data);
  for (const id of references) assert.ok(ids.has(id), `Unresolved entity reference: ${id}`);
}

test('canonical URLs normalize homepage and directory index files', () => {
  for (const base of [config.url, config.url + '/']) {
    for (const path of ['', '/', 'index.html', '/index.html']) {
      assert.equal(absoluteUrl({ ...config, url: base }, path), config.url + '/');
    }
    for (const path of ['download/', '/download/', 'download/index.html']) {
      assert.equal(absoluteUrl({ ...config, url: base }, path), config.url + '/download/');
    }
  }
  assert.equal(absoluteUrl(config, '404.html'), config.url + '/404.html');
  assert.equal(absoluteUrl(config, 'posts/index.html-guide/'), config.url + '/posts/index.html-guide/');
});

test('Chinese paths are encoded once and remain valid canonical URLs', () => {
  const encoded = '/tags/%E6%A1%8C%E5%AE%A0/';
  assert.equal(absoluteUrl(config, '/tags/桌宠/index.html'), config.url + encoded);
  assert.equal(absoluteUrl(config, encoded), config.url + encoded);
  assert.equal(decodeURIComponent(new URL(absoluteUrl(config, 'categories/使用指南/')).pathname),
    '/categories/使用指南/');
});

test('homepage metadata is identified by the canonical route', () => {
  const home = pageInfo(config, { path: 'index.html' });
  assert.equal(home.isHome, true);
  assert.equal(home.canonical, config.url + '/');
  assert.match(home.title, /桌搭子.*Windows.*macOS.*Android/);
  assert.equal(home.crumbs.length, 1);
  assert.equal(home.crumbs[0].url, home.canonical);
  const laterPage = pageInfo(config, { path: 'page/2/index.html', __index: true, current: 2 });
  assert.equal(laterPage.isHome, false, 'A paginated route must not inherit homepage identity');
});

test('tag, category and archive metadata describe their actual collection', () => {
  const cases = [
    [{ path: 'tags/桌宠/index.html', tag: '桌宠' }, '标签：桌宠'],
    [{ path: 'categories/使用指南/index.html', category: '使用指南' }, '分类：使用指南'],
    [{ path: 'archives/index.html', archive: true }, '桌搭子手记'],
    [{ path: 'archives/2026/index.html', archive: true, year: 2026 }, '2026 年'],
    [{ path: 'archives/2026/09/index.html', archive: true, year: 2026, month: 9 }, '2026 年 9 月']
  ];
  const titles = new Set();
  for (const [page, subject] of cases) {
    const info = pageInfo(config, page);
    assert.equal(info.collection, true);
    assert.ok(info.title.includes(subject), `${info.title} should describe ${subject}`);
    assert.notEqual(info.description, config.description);
    assert.equal(info.crumbs.at(-1).url, info.canonical);
    assert.equal(info.crumbs[0].name, '首页');
    titles.add(info.title);
  }
  assert.equal(titles.size, cases.length);
});

test('collection pagination has its own title, description and canonical URL', () => {
  const first = pageInfo(config, { path: 'tags/互动/index.html', tag: '互动' });
  const second = pageInfo(config, { path: 'tags/互动/page/2/index.html', tag: '互动', current: 2 });
  assert.notEqual(second.title, first.title);
  assert.notEqual(second.description, first.description);
  assert.match(second.title, /第 2 页/);
  assert.match(second.description, /第 2 页/);
  assert.equal(second.canonical, absoluteUrl(config, 'tags/互动/page/2/'));
});

test('normal pages retain their editorial metadata and 404 detection is precise', () => {
  const info = pageInfo(config, {
    path: 'about/index.html', title: '关于桌搭子', description: '官方产品介绍。'
  });
  assert.equal(info.title, '关于桌搭子｜桌搭子');
  assert.equal(info.description, '官方产品介绍。');
  assert.equal(info.collection, false);
  assert.equal(pageInfo(config, { path: '404.html' }).is404, true);
  assert.equal(pageInfo(config, { path: 'posts/404-tips/index.html' }).is404, false);
});

test('only an explicit front matter update changes the editorial modification date', () => {
  const date = new Date('2026-08-04T03:20:00.000Z');
  const updated = new Date('2026-09-20T08:00:00.000Z');
  for (const newline of ['\n', '\r\n']) {
    const raw = ['---', 'title: 使用指南', 'updated: 2026-09-20 16:00:00', '---', '正文'].join(newline);
    assert.equal(editorialDate({ raw, date, updated }), updated.toISOString());
  }
  const raw = '---\ntitle: 使用指南\n---\n正文中的示例：\nupdated: 2099-01-01';
  assert.equal(editorialDate({ raw, date, updated }), date.toISOString());
  assert.equal(editorialDate({ date, updated }), date.toISOString());
});

test('filesystem timestamp changes do not fabricate editorial updates', () => {
  const page = { raw: '---\ntitle: 手记\n---\n正文', date: new Date('2026-08-04T03:20:00Z') };
  const first = editorialDate({ ...page, updated: new Date('2026-09-20T07:29:56Z') });
  const second = editorialDate({ ...page, updated: new Date('2026-10-09T12:30:00Z') });
  assert.equal(first, '2026-08-04T03:20:00.000Z');
  assert.equal(first, second);
  assert.equal(editorialDate({}), undefined);
  assert.equal(editorialDate({ date: new Date('invalid') }), undefined);
});

test('JSON-LD serialization cannot close its script element and remains lossless JSON', () => {
  const value = { name: '</script><script>alert("x")</script>', answer: '第一行\u2028第二行\u2029第三行', symbol: 'A < B & C' };
  const encoded = serializeJsonLd(value);
  assert.equal(encoded.includes('<'), false);
  assert.equal(encoded.includes('\u2028'), false);
  assert.equal(encoded.includes('\u2029'), false);
  assert.ok(encoded.includes('\\u003c/script>'));
  assert.ok(encoded.includes('\\u2028'));
  assert.ok(encoded.includes('\\u2029'));
  assert.deepEqual(JSON.parse(encoded), value);
});

test('FAQ entities retain the supplied answers and directly addressable fragments', () => {
  const data = dataFor({ path: 'faq/index.html', layout: 'faq', title: '常见问题' }, questions);
  const faq = entity(data, 'FAQPage');
  assert.equal(faq.mainEntity.length, questions.length);
  for (const [index, question] of questions.entries()) {
    const rendered = faq.mainEntity[index];
    assert.equal(rendered['@type'], 'Question');
    assert.equal(rendered.name, question.question);
    assert.equal(rendered.acceptedAnswer.text, question.answer);
    assert.equal(rendered.acceptedAnswer['@type'], 'Answer');
    assert.equal(rendered.acceptedAnswer.url, config.url + '/faq/#' + question.id);
    assert.equal(rendered['@id'], rendered.acceptedAnswer.url);
  }
  assertResolvedReferences(data);
});

test('commercial pages distinguish the free offer from permanent activation', () => {
  const pages = [
    { path: 'index.html' },
    { path: 'activate/index.html', layout: 'activate' },
    { path: 'download/index.html', layout: 'download' },
    { path: 'faq/index.html', layout: 'faq' }
  ];
  for (const page of pages) {
    const software = entity(dataFor(page, questions), 'SoftwareApplication');
    assert.equal(software.offers.length, 2);
    const free = software.offers.find(offer => Number(offer.price) === 0);
    const paid = software.offers.find(offer => Number(offer.price) === 6.88);
    assert.ok(free, 'The free tier should remain explicit');
    assert.ok(paid, 'Permanent activation must have its own price');
    assert.equal(free.name, product.free.name);
    assert.equal(free.description, product.free.description);
    assert.equal(free.url, config.url + '/download/');
    assert.equal(paid.name, product.activation.name);
    assert.equal(paid.description, product.activation.description);
    assert.equal(paid.url, config.url + '/activate/');
    for (const offer of software.offers) {
      assert.equal(offer['@type'], 'Offer');
      assert.equal(offer.priceCurrency, 'CNY');
    }
  }
});

test('article metadata links the webpage, product and publisher without dangling references', () => {
  const page = {
    path: 'posts/hello/index.html', layout: 'post', title: '桌搭子使用指南',
    raw: '---\ntitle: 桌搭子使用指南\n---\n正文',
    date: new Date('2026-08-04T03:20:00Z'), updated: new Date('2026-10-09T12:30:00Z')
  };
  const data = dataFor(page);
  const article = entity(data, 'BlogPosting');
  const webpage = entity(data, 'WebPage');
  const software = entity(data, 'SoftwareApplication');
  const publisher = entity(data, 'Organization');
  assert.deepEqual(webpage.mainEntity, { '@id': article['@id'] });
  assert.deepEqual(article.mainEntityOfPage, { '@id': webpage['@id'] });
  assert.deepEqual(article.about, { '@id': software['@id'] });
  assert.deepEqual(article.author, { '@id': publisher['@id'] });
  assert.deepEqual(article.publisher, { '@id': publisher['@id'] });
  assert.equal(article.datePublished, '2026-08-04T03:20:00.000Z');
  assert.equal(article.dateModified, article.datePublished);
  assert.equal(article.url, config.url + '/posts/hello/');
  assert.equal(article.headline, page.title);
  assert.equal(software.offers, undefined, 'Historical posts should not embed current pricing offers');
  assertResolvedReferences(data);
});

test('article graphs honor explicit updates without changing their publication date', () => {
  const data = dataFor({
    path: 'posts/guide/', layout: 'post', title: '玩法指南',
    raw: '---\nupdated: 2026-09-20\n---\n正文',
    date: new Date('2026-08-04T03:20:00Z'), updated: new Date('2026-09-20T08:00:00Z')
  });
  const article = entity(data, 'BlogPosting');
  assert.equal(article.datePublished, '2026-08-04T03:20:00.000Z');
  assert.equal(article.dateModified, '2026-09-20T08:00:00.000Z');
});

test('collection entities list exactly the current page posts with canonical item URLs', () => {
  const posts = [
    { title: '第二篇', path: 'posts/second/index.html' },
    { title: '第一篇', path: 'posts/中文标题/index.html' }
  ];
  for (const collection of [posts, { toArray: () => posts }]) {
    const data = dataFor({ path: 'archives/2026/', archive: true, year: 2026, posts: collection });
    const page = entity(data, 'CollectionPage');
    assert.equal(page.mainEntity['@type'], 'ItemList');
    assert.deepEqual(page.mainEntity.itemListElement.map(item => item.name), posts.map(post => post.title));
    assert.deepEqual(page.mainEntity.itemListElement.map(item => item.position), [1, 2]);
    assert.deepEqual(page.mainEntity.itemListElement.map(item => item.url), posts.map(post => absoluteUrl(config, post.path)));
    assert.equal(data['@graph'].some(item => item['@type'] === 'BlogPosting'), false);
    const breadcrumbs = entity(data, 'BreadcrumbList');
    assert.deepEqual(breadcrumbs.itemListElement.map(item => item.position), [1, 2, 3]);
    assert.equal(breadcrumbs.itemListElement[1].item, config.url + '/archives/');
    assert.equal(breadcrumbs.itemListElement.at(-1).item, page.url);
    assertResolvedReferences(data);
  }
});

test('entity IDs remain stable across pages and product data carries no invented ratings', () => {
  const home = dataFor({ path: 'index.html' });
  const download = dataFor({ path: 'download/index.html', layout: 'download' });
  for (const type of ['Organization', 'WebSite', 'SoftwareApplication']) {
    assert.equal(entity(home, type)['@id'], entity(download, type)['@id']);
  }
  const software = entity(home, 'SoftwareApplication');
  assert.equal(software.operatingSystem, 'Windows, macOS, Android');
  assert.deepEqual(software.featureList, product.features);
  assert.equal(software.aggregateRating, undefined);
  assert.equal(software.review, undefined);
  assert.equal(home['@graph'].some(item => item['@type'] === 'BreadcrumbList'), false);
  assertResolvedReferences(home);
  assertResolvedReferences(download);
});
