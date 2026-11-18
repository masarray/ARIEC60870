/* Copyright 2026 Ari Sulistiono
 * SPDX-License-Identifier: Apache-2.0
 * The public Wiki reads the exact same JSON packaged with the offline WPF Help Center.
 * No second hardcoded article list, external JS dependency or HTML injection. */
(() => {
  'use strict';
  const host = document.getElementById('arsee-handbook');
  if (!host) return;
  const lang = document.documentElement.lang === 'id' ? 'id' : 'en';
  const suffix = lang === 'id' ? 'Id' : 'En';
  const labels = lang === 'id'
    ? { search: 'Cari materi IEC, polling, redundansi...', chapters: 'Daftar bab', empty: 'Tidak ada bab yang cocok.', sources: 'Sumber dan referensi', failed: 'Katalog belum bisa dibuka. Periksa koneksi atau buka ulang halaman.', caveat: 'Panduan edukasi orisinal, bukan pengganti buku IEC resmi atau interoperability list proyek.' }
    : { search: 'Search IEC, polling, redundancy...', chapters: 'Chapters', empty: 'No matching chapters.', sources: 'Sources and references', failed: 'The learning catalogue could not be loaded. Check connection or reload.', caveat: 'Original learning material, not a substitute for licensed IEC standards or the project interoperability list.' };
  const lookup = (obj, key) => obj[key + suffix] || '';
  const node = (tag, className, content) => {
    const el = document.createElement(tag);
    if (className) el.className = className;
    if (content !== undefined) el.textContent = content;
    return el;
  };
  const add = (parent, tag, className, content) => {
    const el = node(tag, className, content);
    parent.appendChild(el);
    return el;
  };
  const shell = add(host, 'div', 'learning-shell');
  const nav = add(shell, 'aside', 'learning-nav');
  add(nav, 'h3', 'learning-nav-title', labels.chapters);
  const input = add(nav, 'input', 'learning-search');
  input.type = 'search';
  input.placeholder = labels.search;
  input.setAttribute('aria-label', labels.search);
  input.autocomplete = 'off';
  const menu = add(nav, 'nav', 'learning-menu');
  menu.setAttribute('aria-label', labels.chapters);
  const article = add(shell, 'article', 'learning-article');
  article.id = 'engineering-handbook-article';
  const status = add(host, 'p', 'learning-status', labels.caveat);

  fetch(host.dataset.source, { cache: 'default' })
    .then(response => {
      if (!response.ok) throw new Error('HTTP ' + response.status);
      return response.json();
    })
    .then(book => {
      if (book.schemaVersion !== 1 || !Array.isArray(book.chapters) || book.chapters.length === 0)
        throw new Error('Unexpected handbook format');
      const chapters = book.chapters;
      const keys = new Set();
      chapters.forEach(ch => {
        if (!/^learn-[a-z0-9-]+$/.test(ch.key) || keys.has(ch.key) ||
            !Array.isArray(ch.sections) || !ch.sections.length)
          throw new Error('Invalid or duplicate handbook chapter');
        keys.add(ch.key);
      });
      status.textContent = book['notice' + suffix] || labels.caveat;
      let current = chapters.find(ch => '#' + ch.key === window.location.hash) || chapters[0];
      const renderArticle = ch => {
        current = ch;
        article.replaceChildren();
        add(article, 'p', 'learning-overline', lang === 'id' ? 'BUKU ENGINEER · IEC 60870' : 'ENGINEER HANDBOOK · IEC 60870');
        add(article, 'h3', 'learning-article-title', lookup(ch, 'title'));
        add(article, 'p', 'learning-article-lead', lookup(ch, 'summary'));
        ch.sections.forEach((s, i) => {
          const section = add(article, 'section', 'learning-chapter-section');
          add(section, 'h4', '', lookup(s, 'heading'));
          (s['paragraphs' + suffix] || []).forEach(p => add(section, 'p', '', p));
          if ((s['bullets' + suffix] || []).length) {
            const ul = add(section, 'ul', 'learning-bullets');
            s['bullets' + suffix].forEach(p => add(ul, 'li', '', p));
          }
          if ((s['frames' + suffix] || []).length)
            add(section, 'pre', 'learning-frames', s['frames' + suffix].join('\n'));
        });
        if (Array.isArray(ch.sources) && ch.sources.length) {
          const section = add(article, 'section', 'learning-references');
          add(section, 'h4', '', labels.sources);
          const list = add(section, 'ul');
          ch.sources.forEach(link => {
            try {
              const url = new URL(link);
              if (url.protocol !== 'https:') return;
              const item = add(list, 'li');
              const anchor = add(item, 'a', '', url.hostname + url.pathname);
              anchor.href = url.href;
              anchor.target = '_blank';
              anchor.rel = 'noopener noreferrer';
            } catch { /* Ignore malformed untrusted URI. */ }
          });
        }
      };
      const renderMenu = () => {
        const q = input.value.trim().toLocaleLowerCase();
        menu.replaceChildren();
        let visible = 0;
        chapters.forEach(ch => {
          const haystack = [ch.titleEn, ch.titleId, ch.summaryEn, ch.summaryId,
            ch.searchText, ...ch.sections.flatMap(s => [
              s.headingEn, s.headingId,
              ...(s.paragraphsEn || []), ...(s.paragraphsId || []),
              ...(s.bulletsEn || []), ...(s.bulletsId || [])
            ])].join(' ').toLocaleLowerCase();
          if (q && !q.split(/\s+/).every(term => haystack.includes(term))) return;
          visible += 1;
          const button = add(menu, 'button', 'learning-chapter-button', lookup(ch, 'title'));
          button.type = 'button';
          button.dataset.chapter = ch.key;
          button.setAttribute('aria-current', ch.key === current.key ? 'page' : 'false');
          button.addEventListener('click', () => {
            renderArticle(ch);
            renderMenu();
            history.replaceState(null, '', '#' + ch.key);
            if (window.matchMedia('(max-width: 740px)').matches)
              article.scrollIntoView({ behavior: 'auto', block: 'start' });
          });
        });
        if (!visible) add(menu, 'p', 'learning-no-result', labels.empty);
      };
      renderArticle(current);
      renderMenu();
      input.addEventListener('input', renderMenu);
      window.addEventListener('hashchange', () => {
        const target = chapters.find(ch => '#' + ch.key === location.hash);
        if (target) { renderArticle(target); renderMenu(); }
      });
    })
    .catch(() => {
      nav.replaceChildren();
      article.replaceChildren();
      add(article, 'p', 'learning-load-error', labels.failed);
    });
})();
