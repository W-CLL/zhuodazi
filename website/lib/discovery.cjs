'use strict';

function absoluteUrl(config, path = '/') {
  const base = String(config.url).replace(/\/$/, '') + '/';
  return new URL(String(path).replace(/(^|\/)index\.html$/, '$1'), base).href;
}

function editorialDate(page) {
  // Hexo defaults updated to filesystem mtime; only authored dates are evidence.
  const frontMatter = String(page.raw || '').match(/^---\r?\n([\s\S]*?)\r?\n---/);
  const explicitUpdate = frontMatter && /^updated\s*:/m.test(frontMatter[1]);
  const value = explicitUpdate ? page.updated : page.date;
  if (!value) return undefined;
  const date = new Date(value.valueOf());
  return Number.isNaN(date.valueOf()) ? undefined : date.toISOString();
}

function pageInfo(config, page) {
  const canonical = absoluteUrl(config, page.path || '/');
  const isHome = new URL(canonical).pathname === new URL(absoluteUrl(config)).pathname;
  const is404 = /(?:^|\/)404\.html$/.test(page.path || '');
  const collection = Boolean(page.archive || page.tag || page.category);
  let label = page.title || config.title;
  let description = page.description || page.excerpt || config.description;
  if (isHome) label = '桌搭子官网：Windows、macOS、Android 桌面宠物';
  else if (page.tag) {
    label = `标签：${page.tag}`;
    description = `桌搭子「${page.tag}」相关文章，包含使用经验、玩法与版本记录。`;
  } else if (page.category) {
    label = `分类：${page.category}`;
    description = `桌搭子「${page.category}」文章合集，按发布时间浏览官方说明。`;
  } else if (page.archive) {
    const period = page.year ? `${page.year} 年${page.month ? ` ${page.month} 月` : ''}` : '';
    label = period ? `${period}桌搭子手记` : '桌搭子手记：使用指南与更新记录';
    description = `${period}桌搭子的使用指南、玩法文章与历史更新记录；最新安装包请以官方下载页为准。`;
  }
  if (page.current > 1) {
    label += `（第 ${page.current} 页）`;
    description += ` 当前为第 ${page.current} 页。`;
  }
  const crumbs = [{ name: '首页', url: absoluteUrl(config) }];
  if (!isHome && !is404) {
    if (page.layout === 'post' || page.tag || page.category || (page.archive && page.year)) {
      crumbs.push({ name: '手记', url: absoluteUrl(config, 'archives/') });
    }
    crumbs.push({ name: label, url: canonical });
  }
  return { canonical, isHome, is404, collection, label, description,
    title: isHome ? label : `${label}｜桌搭子`, crumbs };
}

function structuredData(config, product, questions, page, info, description, image) {
  const root = absoluteUrl(config);
  const organizationId = root + '#publisher';
  const websiteId = root + '#website';
  const softwareId = root + '#software';
  const pageId = info.canonical + '#webpage';
  const isPost = page.layout === 'post';
  const isFAQ = page.layout === 'faq';
  const graph = [
    { '@type': 'Organization', '@id': organizationId, name: product.name,
      url: root, logo: { '@type': 'ImageObject', url: absoluteUrl(config, 'images/app-icon.png') } },
    { '@type': 'WebSite', '@id': websiteId, name: product.name, alternateName: product.alternate_name,
      url: root, inLanguage: 'zh-CN', publisher: { '@id': organizationId } },
    { '@type': isFAQ ? 'FAQPage' : info.collection ? 'CollectionPage' : 'WebPage',
      '@id': pageId, url: info.canonical, name: info.title, description,
      inLanguage: 'zh-CN', isPartOf: { '@id': websiteId }, about: { '@id': softwareId },
      primaryImageOfPage: { '@type': 'ImageObject', url: image } }
  ];
  const webPage = graph[2];
  if (!info.isHome && !info.is404) {
    webPage.breadcrumb = { '@id': info.canonical + '#breadcrumb' };
    graph.push({ '@type': 'BreadcrumbList', '@id': info.canonical + '#breadcrumb',
      itemListElement: info.crumbs.map((crumb, i) => ({ '@type': 'ListItem', position: i + 1, name: crumb.name, item: crumb.url })) });
  }
  if (isPost) {
    const article = { '@type': 'BlogPosting', '@id': info.canonical + '#article',
      headline: page.title, description, image, url: info.canonical,
      inLanguage: 'zh-CN', mainEntityOfPage: { '@id': pageId }, about: { '@id': softwareId },
      author: { '@id': organizationId }, publisher: { '@id': organizationId } };
    if (page.date) article.datePublished = new Date(page.date.valueOf()).toISOString();
    const modified = editorialDate(page);
    if (modified) article.dateModified = modified;
    webPage.mainEntity = { '@id': article['@id'] };
    graph.push(article);
  } else if (isFAQ) {
    webPage.mainEntity = questions.map(item => ({ '@type': 'Question',
      '@id': info.canonical + '#' + item.id, name: item.question,
      acceptedAnswer: { '@type': 'Answer', text: item.answer, url: info.canonical + '#' + item.id } }));
  } else if (info.collection && page.posts) {
    const posts = page.posts.toArray ? page.posts.toArray() : page.posts;
    webPage.mainEntity = { '@type': 'ItemList', itemListElement: posts.map((post, i) => ({
      '@type': 'ListItem', position: i + 1, name: post.title, url: absoluteUrl(config, post.path) })) };
  }
  const software = { '@type': 'SoftwareApplication', '@id': softwareId,
    name: product.name, alternateName: product.alternate_name,
    applicationCategory: 'EntertainmentApplication', operatingSystem: product.platforms.join(', '),
    url: root, downloadUrl: absoluteUrl(config, 'download/'), description: product.description,
    image: absoluteUrl(config, 'images/promo/hello.webp'), featureList: product.features,
    publisher: { '@id': organizationId } };
  if (info.isHome || ['activate', 'download', 'faq'].includes(page.layout)) {
    software.offers = [
      { '@type': 'Offer', name: product.free.name, price: product.free.price, priceCurrency: product.activation.currency,
        url: absoluteUrl(config, 'download/'), description: product.free.description },
      { '@type': 'Offer', name: product.activation.name, price: product.activation.price, priceCurrency: product.activation.currency,
        url: absoluteUrl(config, 'activate/'), description: product.activation.description }
    ];
  }
  graph.push(software);
  return { '@context': 'https://schema.org', '@graph': graph };
}

function serializeJsonLd(value) {
  return JSON.stringify(value).replace(/</g, '\\u003c').replace(/\u2028/g, '\\u2028').replace(/\u2029/g, '\\u2029');
}

module.exports = { absoluteUrl, editorialDate, pageInfo, structuredData, serializeJsonLd };
