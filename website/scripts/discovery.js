'use strict';

const { absoluteUrl, editorialDate, pageInfo, structuredData, serializeJsonLd } = require('../lib/discovery.cjs');

hexo.extend.helper.register('discovery_info', function(page) { return pageInfo(this.config, page); });
hexo.extend.helper.register('editorial_date', editorialDate);
hexo.extend.helper.register('discovery_json', function(page, info, description, image) {
  return serializeJsonLd(structuredData(this.config, this.site.data.product, this.site.data.questions, page, info, description, image));
});

hexo.extend.generator.register('ai-reading', function(locals) {
  const product = locals.data.product;
  const questions = locals.data.questions;
  const posts = locals.posts.sort('date', 'desc').toArray();
  const url = path => absoluteUrl(this.config, path);
  const intro = `# ${product.name}\n\n> ${product.description}\n\n${product.free.description}\n\n${product.activation.description}\n\n`;
  const official = product.links.map(link => `- [${link.name}](${url(link.path)}): ${link.description}`).join('\n');
  const articles = posts.map(post => `- [${post.title}](${url(post.path)}): ${post.description || post.title}`).join('\n');
  const index = intro + `## 官方页面\n\n${official}\n\n## 玩法与更新文章\n\n${articles}\n\n## 阅读说明\n\n历史更新文章只代表该次发布，最新安装包请以下载页为准。各平台功能有差异，具体条件见玩法指南和常见问题。\n\n- [完整文本](${url('llms-full.txt')}): 产品信息、常见问题和文章正文的文本版本。\n`;
  const answers = questions.map(q => `## ${q.question}\n\n${q.answer}\n\n来源：${url('faq/')}#${q.id}\n详细说明：${url(q.link)}\n`).join('\n');
  const bodies = posts.map(post => {
    const content = String(post.raw || '').replace(/^---\r?\n[\s\S]*?\r?\n---\s*/, '')
      .replace(/\]\((\/[^)\s]*)([^)]*)\)/g, (_, path, suffix) => `](${url(path)}${suffix})`);
    return `# ${post.title}\n\n来源：${url(post.path)}\n作者：${post.author || this.config.author}\n发布时间：${post.date.toISOString()}\n内容更新：${editorialDate(post)}\n\n${content}\n`;
  }).join('\n---\n\n');
  return [
    { path: 'llms.txt', data: index },
    { path: 'llms-full.txt', data: index + '\n---\n\n# 常见问题官方解答\n\n' + answers + '\n---\n\n' + bodies }
  ];
});

// Use actual generated HTML routes so archives, taxonomies and pagination cannot drift.
hexo.extend.filter.register('after_generate', async function() {
  const dates = new Map();
  this.locals.get('posts').forEach(post => dates.set(absoluteUrl(this.config, post.path), editorialDate(post)));
  const routes = this.route.list().filter(path => path.endsWith('.html') && !/(?:^|\/)404\.html$/.test(path)).sort();
  const entries = [];
  for (const path of routes) {
    let html = '';
    for await (const chunk of this.route.get(path)) html += chunk.toString();
    if (/<meta\b[^>]*name="robots"[^>]*content="[^"]*noindex/i.test(html)) continue;
    const canonical = absoluteUrl(this.config, path);
    const escaped = canonical.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
    const modified = dates.get(canonical);
    entries.push(`  <url><loc>${escaped}</loc>${modified ? `<lastmod>${modified}</lastmod>` : ''}</url>`);
  }
  this.route.set('sitemap.xml', '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' + entries.join('\n') + '\n</urlset>\n');
});
