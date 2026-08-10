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

  const customizeContent = {
    library: {
      label: '自定义导入',
      title: '把自己的角色带进桌面。',
      copy: '直接添加透明 GIF，或者绑定已有的动图目录。你可以从内置和外部资源库中选择来源，再决定多久随机换一只。',
      points: ['最多添加 3 个自定义桌宠', '最多绑定 3 个外部 GIF 目录', '支持手动切换与定时随机换宠'],
      status: '资源库 / 3 个来源',
      summary: '随机切换：每 5 分钟',
      images: ['/images/pet-reading.gif', '/images/pet-rest.gif', '/images/pet-celebrate.gif'],
      items: ['我的角色.gif', '午后休息.gif', '下班啦.gif'],
      alts: ['读书中的桌搭子', '休息中的桌搭子', '开心庆祝的桌搭子']
    },
    interaction: {
      label: '互动词包',
      title: '让它说话，也说你的话。',
      copy: '把 JSON 或 TXT 词包放进桌搭子，选择一套作为当前互动内容。默认内容、在线补充和自己的台词可以随时切换。',
      points: ['最多保留 5 套互动词包', '支持问候、夸夸、冷笑话和小知识', '在线内容也会缓存为离线包'],
      status: '互动词包 / 128 条台词',
      summary: '当前词包：下班前夸夸',
      images: ['/images/pet-keyboard.gif', '/images/pet-balance.gif', '/images/pet-wave.gif'],
      items: ['今日小问候.json', '下班前夸夸.txt', '冷笑话合集.json'],
      alts: ['趴在键盘前的桌搭子', '保持平衡的桌搭子', '挥手的桌搭子']
    },
    personality: {
      label: '行为与性格',
      title: '安静、活泼，或有一点混乱。',
      copy: '选择活泼、害羞、黏人或混乱性格，再决定是否响应鼠标、是否随机走动，以及窗口是否保持在最上层。',
      points: ['四种性格预设，一键切换', '鼠标响应、随机走动可独立开关', '支持开机启动、点击穿透和窗口行为'],
      status: '行为设置 / 活泼',
      summary: '鼠标互动：开启 · 随机走动：开启',
      images: ['/images/pet-chair.gif', '/images/pet-balance.gif', '/images/pet-celebrate.gif'],
      items: ['活泼 · 会追着鼠标看', '害羞 · 偶尔躲起来', '混乱 · 随机小剧场'],
      alts: ['趴在椅子上的桌搭子', '保持平衡的桌搭子', '开心庆祝的桌搭子']
    },
    theater: {
      label: '小剧场剧本',
      title: '给桌面写一段三分钟的戏。',
      copy: '导入 JSON 剧本后，桌搭子会随机抽取一段演出。靠近、换位、跳跃和对白都会在透明窗口里完成。',
      points: ['最多导入 10 个 JSON 剧本', '每段包含 3-5 轮双人对白', '每次演出随机抽取，随时可以跳过'],
      status: '小剧场 / 10 个剧本',
      summary: '下一场：《键盘争夺战》',
      images: ['/images/pet-celebrate.gif', '/images/pet-reading.gif', '/images/pet-rest.gif'],
      items: ['键盘争夺战.json', '午休联盟.json', '周五下班.json'],
      alts: ['举手庆祝的桌搭子', '读书中的桌搭子', '休息中的桌搭子']
    }
  };

  const customizePanel = document.querySelector('[data-customize-panel]');
  const customizeButtons = document.querySelectorAll('[data-customize]');
  const customizeLabel = document.querySelector('[data-customize-label]');
  const customizeTitle = document.querySelector('[data-customize-title]');
  const customizeCopy = document.querySelector('[data-customize-copy]');
  const customizePoints = document.querySelectorAll('[data-customize-point]');
  const customizeStatus = document.querySelector('[data-customize-status]');
  const customizeSummary = document.querySelector('[data-customize-summary]');
  const customizeImages = document.querySelectorAll('[data-customize-image]');
  const customizeItems = document.querySelectorAll('[data-customize-item]');

  const setCustomizeMode = (key) => {
    const content = customizeContent[key];
    if (!content || !customizePanel) return;

    customizePanel.dataset.customizeTheme = key;
    if (customizeLabel) customizeLabel.textContent = content.label;
    if (customizeTitle) customizeTitle.textContent = content.title;
    if (customizeCopy) customizeCopy.textContent = content.copy;
    if (customizeStatus) customizeStatus.textContent = content.status;
    if (customizeSummary) customizeSummary.textContent = content.summary;
    customizePoints.forEach((point, index) => {
      point.textContent = content.points[index];
    });
    customizeImages.forEach((image, index) => {
      image.src = content.images[index];
      image.alt = content.alts[index];
    });
    customizeItems.forEach((item, index) => {
      item.textContent = content.items[index];
    });
    customizeButtons.forEach((button) => {
      const isSelected = button.dataset.customize === key;
      button.classList.toggle('is-active', isSelected);
      button.setAttribute('aria-selected', String(isSelected));
    });
  };

  customizeButtons.forEach((button) => {
    button.addEventListener('click', () => setCustomizeMode(button.dataset.customize));
  });

  const updateHeader = () => {
    header?.classList.toggle('is-scrolled', window.scrollY > 8);
  };

  updateHeader();
  window.addEventListener('scroll', updateHeader, { passive: true });

  const modeContent = {
    quiet: {
      title: '安静陪伴',
      copy: '把它留在角落，需要时再轻轻回应。适合想沉下心的下午。',
      speech: '我在，慢慢来。',
      image: '/images/pet-focus.gif',
      alt: '桌搭子安静陪伴的动画'
    },
    steady: {
      title: '标准互动',
      copy: '在忙碌和休息之间，偶尔说一句恰到好处的话。',
      speech: '要不要喝口水，再继续？',
      image: '/images/pet-balance.gif',
      alt: '桌搭子标准互动的动画'
    },
    lively: {
      title: '热闹一点',
      copy: '让词包和小剧场多出现一会儿，给桌面添一点热闹。',
      speech: '今天也辛苦啦，给你一个小彩蛋！',
      image: '/images/pet-wave.gif',
      alt: '桌搭子热闹互动的动画'
    }
  };

  const modeButtons = document.querySelectorAll('[data-mode]');
  const modeTitle = document.querySelector('[data-mode-title]');
  const modeCopy = document.querySelector('[data-mode-copy]');
  const modeSpeech = document.querySelector('[data-mode-speech]');
  const modeImage = document.querySelector('[data-mode-image]');

  modeButtons.forEach((button) => {
    button.addEventListener('click', () => {
      const mode = modeContent[button.dataset.mode];
      if (!mode) return;

      modeButtons.forEach((item) => {
        const isSelected = item === button;
        item.classList.toggle('is-active', isSelected);
        item.setAttribute('aria-selected', String(isSelected));
      });

      modeTitle.textContent = mode.title;
      modeCopy.textContent = mode.copy;
      modeSpeech.textContent = mode.speech;
      modeImage.src = mode.image;
      modeImage.alt = mode.alt;
    });
  });

  const speech = document.querySelector('[data-speech]');
  const speechLines = [
    '认真工作的时候，我就在这里。',
    '存个文件吧，今天已经做得很好。',
    '休息一分钟，再慢慢继续。'
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
          element.textContent = element.textContent.replace(/\d+(?:\.\d+){1,3}(?:-[\w.-]+)?$/, release.version);
        });
        const size = formatBytes(release.size);
        if (size) document.querySelectorAll(`[data-release-size="${target}"]`).forEach((element) => {
          element.textContent = size;
        });
      }
    })
    .catch(() => {});
})();
