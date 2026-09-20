(() => {
  const tabs = [...document.querySelectorAll('[data-scene]')];
  const activate = (tab, focus = false) => {
    tabs.forEach((item) => {
      const selected = item === tab;
      item.setAttribute('aria-selected', String(selected));
      item.tabIndex = selected ? 0 : -1;
      document.getElementById(item.getAttribute('aria-controls')).hidden = !selected;
    });
    if (focus) tab.focus();
  };
  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => activate(tab));
    tab.addEventListener('keydown', (event) => {
      let next;
      if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
      if (event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
      if (event.key === 'Home') next = 0;
      if (event.key === 'End') next = tabs.length - 1;
      if (next === undefined) return;
      event.preventDefault();
      activate(tabs[next], true);
    });
  });

  const links = document.querySelectorAll('[data-lightbox]');
  if (!links.length || typeof HTMLDialogElement === 'undefined') return;
  const dialog = document.createElement('dialog');
  dialog.className = 'image-dialog';
  dialog.setAttribute('aria-labelledby', 'image-dialog-caption');
  const bar = document.createElement('div');
  bar.className = 'image-dialog-bar';
  const caption = document.createElement('p');
  caption.id = 'image-dialog-caption';
  const close = document.createElement('button');
  close.type = 'button';
  close.textContent = '关闭 ×';
  close.setAttribute('aria-label', '关闭大图');
  const image = document.createElement('img');
  bar.append(caption, close);
  dialog.append(bar, image);
  document.body.append(dialog);
  let trigger;
  let previousOverflow;
  links.forEach((link) => link.addEventListener('click', (event) => {
    event.preventDefault();
    trigger = link;
    image.src = link.href;
    image.alt = link.querySelector('img').alt;
    caption.textContent = image.alt;
    previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    dialog.showModal();
  }));
  close.addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', (event) => {
    const rect = dialog.getBoundingClientRect();
    if (event.target === dialog && (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom)) dialog.close();
  });
  dialog.addEventListener('close', () => {
    document.body.style.overflow = previousOverflow;
    trigger?.focus({ preventScroll: true });
  });
})();
