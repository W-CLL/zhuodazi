(() => {
  const categories = document.querySelector('[data-share-categories]');
  if (!categories) return;
  const items = document.querySelector('[data-share-items]'), status = document.querySelector('[data-share-status]');
  const refresh = document.querySelector('[data-share-refresh]');
  let data = [], selected, loading = false;
  function render() {
    categories.replaceChildren(); items.replaceChildren();
    for (const category of data) {
      const button = document.createElement('button'); button.type = 'button'; button.className = 'button button-secondary';
      button.textContent = category.name; button.setAttribute('aria-pressed', String(category.id === selected));
      button.addEventListener('click', () => { selected = category.id; render(); }); categories.append(button);
    }
    const category = data.find(value => value.id === selected);
    status.textContent = !data.length ? '暂无分享，过段时间再来看看吧。' : !category?.items?.length ? '这个分类还没有分享。' : '';
    for (const item of category?.items || []) {
      let url;
      try { url = new URL(item.url); } catch { continue; }
      if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password) continue;
      const link = document.createElement('a'); link.className = 'share-card'; link.href = url.href; link.target = '_blank'; link.rel = 'noopener noreferrer';
      const title = document.createElement('h2'); title.textContent = item.title;
      const description = document.createElement('p'); description.textContent = item.description;
      const domain = document.createElement('span'); domain.className = 'share-domain'; domain.textContent = `${url.hostname} ↗`;
      link.append(title, description, domain); items.append(link);
    }
  }
  async function load() {
    if (loading) return;
    loading = true; refresh.disabled = true; status.textContent = '正在加载分享…';
    try {
      const response = await fetch('https://in.desktoppet.online/api/public/author-shares', { cache: 'no-store', signal: AbortSignal.timeout(15000) });
      if (!response.ok) throw new Error();
      const result = await response.json();
      if (!Array.isArray(result.categories)) throw new Error();
      data = result.categories; if (!data.some(category => category.id === selected)) selected = data[0]?.id;
      render();
    } catch { status.textContent = '暂时无法加载，请稍后刷新。'; }
    finally { loading = false; refresh.disabled = false; }
  }
  refresh.addEventListener('click', load); load();
})();
