(() => {
  const root = document.documentElement;
  const body = document.body;
  const header = document.querySelector('[data-site-header]');
  const navToggle = document.querySelector('[data-nav-toggle]');
  const themeToggle = document.querySelector('[data-theme-toggle]');
  const storedTheme = window.localStorage.getItem('deskpet-theme');

  const setTheme = (theme) => {
    root.dataset.theme = theme;
    window.localStorage.setItem('deskpet-theme', theme);
  };

  if (storedTheme) {
    root.dataset.theme = storedTheme;
  }

  themeToggle?.addEventListener('click', () => {
    setTheme(root.dataset.theme === 'dark' ? 'light' : 'dark');
  });

  navToggle?.addEventListener('click', () => {
    const isOpen = body.classList.toggle('nav-open');
    navToggle.setAttribute('aria-expanded', String(isOpen));
    navToggle.setAttribute('aria-label', isOpen ? '关闭导航菜单' : '打开导航菜单');
  });

  document.querySelectorAll('[data-site-nav] a').forEach((link) => {
    link.addEventListener('click', () => {
      body.classList.remove('nav-open');
      navToggle?.setAttribute('aria-expanded', 'false');
      navToggle?.setAttribute('aria-label', '打开导航菜单');
    });
  });

  const updateHeader = () => {
    header?.classList.toggle('is-scrolled', window.scrollY > 8);
  };

  updateHeader();
  window.addEventListener('scroll', updateHeader, { passive: true });

  const speech = document.querySelector('[data-speech]');
  const speechLines = [
    '搭子发来一张：下班了吗？',
    '这张表情先替我去看看你。',
    '你暂时离线，它会等你回来。'
  ];
  let speechIndex = 0;

  if (speech && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    window.setInterval(() => {
      speechIndex = (speechIndex + 1) % speechLines.length;
      speech.textContent = speechLines[speechIndex];
    }, 4800);
  }

  const revealItems = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible');
          observer.unobserve(entry.target);
        }
      });
    }, { threshold: 0.12 });
    revealItems.forEach((item) => observer.observe(item));
  } else {
    revealItems.forEach((item) => item.classList.add('is-visible'));
  }

  const analyticsEndpoint = 'https://in.desktoppet.online/api/analytics/events';
  const getStoredId = (storage, key) => {
    try {
      const existing = storage.getItem(key);
      if (existing && /^[A-Za-z0-9_-]{12,100}$/.test(existing)) return existing;
      const created = (window.crypto?.randomUUID?.() || `web-${Date.now()}-${Math.random().toString(36).slice(2)}`)
        .replace(/[^A-Za-z0-9_-]/g, '-');
      storage.setItem(key, created);
      return created;
    } catch {
      return `web-${Date.now()}-${Math.random().toString(36).slice(2)}`;
    }
  };
  const visitorId = getStoredId(window.localStorage, 'deskpet-visitor-id');
  const sessionId = getStoredId(window.sessionStorage, 'deskpet-session-id');
  const query = new URLSearchParams(window.location.search);
  const analyticsContext = {
    visitorId,
    sessionId,
    pagePath: window.location.pathname,
    referrer: document.referrer,
    utmSource: query.get('utm_source') || '',
    utmMedium: query.get('utm_medium') || '',
    utmCampaign: query.get('utm_campaign') || ''
  };

  const track = (type, extra = {}) => {
    const eventId = `web-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
    const body = {
      events: [{
        ...analyticsContext,
        ...extra,
        eventId,
        type,
        occurredAt: new Date().toISOString()
      }]
    };
    const request = fetch(analyticsEndpoint, {
      method: 'POST',
      mode: 'cors',
      keepalive: true,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    request.catch(() => {
      try {
        if (navigator.sendBeacon) {
          navigator.sendBeacon(analyticsEndpoint, new Blob([JSON.stringify(body)], { type: 'application/json' }));
        }
      } catch {}
    });
  };

  track('page_view');
  document.querySelectorAll('[data-analytics-download]').forEach((link) => {
    link.addEventListener('click', () => track('download_click', {
      platform: link.dataset.platform || '',
      architecture: link.dataset.architecture || '',
      pagePath: window.location.pathname
    }));
  });

  const formatBytes = (value) => {
    const bytes = Number(value);
    if (!Number.isFinite(bytes)) return '';
    if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
    if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${bytes} B`;
  };

  const resourceList = document.querySelector('[data-resource-list]');
  const resourceState = document.querySelector('[data-resource-state]');
  const resourceFilters = document.querySelectorAll('[data-resource-filter]');
  let resourcePacks = [];
  let resourceCategory = 'all';
  const resourceCategoryLabel = (category) => category === 'theater-scripts' ? '小剧场剧本' : '互动词包';
  const formatResourceDate = (value) => {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' }).format(date);
  };
  const renderResourcePacks = () => {
    if (!resourceList || !resourceState) return;
    const visible = resourceCategory === 'all'
      ? resourcePacks
      : resourcePacks.filter((pack) => pack.category === resourceCategory);
    resourceList.replaceChildren();
    resourceList.hidden = visible.length === 0;
    resourceState.hidden = visible.length > 0;
    if (visible.length === 0) {
      resourceState.querySelector('.resource-state-mark').textContent = '—';
      resourceState.querySelector('p').textContent = resourcePacks.length
        ? '这个分类暂时没有资源包'
        : '资源包正在整理中，稍后再来看看';
      return;
    }

    for (const pack of visible) {
      const card = document.createElement('article');
      card.className = 'resource-pack-card';
      const header = document.createElement('div');
      header.className = 'resource-pack-card-header';
      const title = document.createElement('h3');
      title.textContent = pack.title;
      const category = document.createElement('span');
      category.className = 'resource-pack-category';
      category.dataset.category = pack.category;
      category.textContent = resourceCategoryLabel(pack.category);
      header.append(title, category);

      const description = document.createElement('p');
      description.className = 'resource-pack-description';
      description.textContent = pack.description;

      const footer = document.createElement('div');
      footer.className = 'resource-pack-card-footer';
      const meta = document.createElement('div');
      meta.className = 'resource-pack-meta';
      const size = document.createElement('span');
      size.textContent = formatBytes(pack.size);
      const date = document.createElement('span');
      date.textContent = formatResourceDate(pack.createdAt);
      meta.append(size, date);
      const download = document.createElement('a');
      download.className = 'button button-primary';
      download.href = pack.url;
      download.textContent = '下载 ZIP ↓';
      download.addEventListener('click', () => track('resource_download_click', {
        pagePath: window.location.pathname
      }));
      footer.append(meta, download);
      card.append(header, description, footer);
      resourceList.append(card);
    }
  };

  resourceFilters.forEach((button) => {
    button.addEventListener('click', () => {
      resourceCategory = button.dataset.resourceFilter || 'all';
      resourceFilters.forEach((item) => {
        const selected = item === button;
        item.classList.toggle('is-active', selected);
        item.setAttribute('aria-selected', String(selected));
      });
      renderResourcePacks();
    });
  });

  if (resourceList) {
    fetch('https://in.desktoppet.online/api/public/resource-packs', { mode: 'cors' })
      .then((response) => response.ok ? response.json() : Promise.reject(new Error('resource request failed')))
      .then((payload) => {
        resourcePacks = Array.isArray(payload?.packs) ? payload.packs : [];
        renderResourcePacks();
      })
      .catch(() => {
        resourcePacks = [];
        renderResourcePacks();
        if (resourceState) resourceState.querySelector('p').textContent = '暂时无法读取资源包，请稍后再试';
      });
  }

  fetch('https://in.desktoppet.online/api/public/downloads', { mode: 'cors' })
    .then((response) => response.ok ? response.json() : null)
    .then((payload) => {
      for (const release of payload?.downloads || []) {
        const target = `${release.platform}/${release.architecture}`;
        document.querySelectorAll(`[data-release-version="${target}"]`).forEach((element) => {
          element.textContent = element.textContent.replace(/(?:\d+(?:\.\d+){1,3}(?:-[\w.-]+)?|最新版)$/, release.version);
        });
        const size = formatBytes(release.size);
        if (size) document.querySelectorAll(`[data-release-size="${target}"]`).forEach((element) => {
          element.textContent = size;
        });
      }
    })
    .catch(() => {});

  const xianyuLinks = document.querySelectorAll('[data-xianyu-link]');
  if (xianyuLinks.length) {
    fetch('https://in.desktoppet.online/api/public/site-settings', { mode: 'cors' })
      .then((response) => response.ok ? response.json() : null)
      .then((settings) => {
        if (!settings?.xianyuUrl) return;
        xianyuLinks.forEach((link) => {
          link.href = settings.xianyuUrl;
          link.hidden = false;
        });
      })
      .catch(() => {});
  }
})();
