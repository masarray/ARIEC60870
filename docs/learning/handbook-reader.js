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
      const hashSelection = () => {
        const [key, sectionToken] = decodeURIComponent(location.hash.replace(/^#/, '')).split('/');
        const chapter = chapters.find(ch => ch.key === key) || chapters[0];
        const section = /^s[0-9]+$/.test(sectionToken || '') ? Number(sectionToken.slice(1)) - 1 : -1;
        return { chapter, section };
      };
      let selected = hashSelection();
      let current = selected.chapter;
      let currentSection = selected.section;
      let currentElements = [];
      const selectChapter = (chapter, section = -1, saveHash = true) => {
        current = chapter;
        currentSection = section;
        renderArticle(chapter);
        renderMenu();
        if (saveHash) history.replaceState(null, '', '#' + chapter.key + (section >= 0 ? '/s' + (section + 1) : ''));
        if (section >= 0 && currentElements[section])
          currentElements[section].scrollIntoView({ behavior: 'auto', block: 'start' });
        else if (window.matchMedia('(max-width: 740px)').matches)
          article.scrollIntoView({ behavior: 'auto', block: 'start' });
      };
      const renderArticle = ch => {
        article.replaceChildren();
        currentElements = [];
        add(article, 'p', 'learning-overline', (lang === 'id' ? 'BUKU ENGINEER · BAB ' : 'ENGINEER HANDBOOK · CHAPTER ') + (ch.number || ''));
        add(article, 'h3', 'learning-article-title', lookup(ch, 'title'));
        add(article, 'p', 'learning-article-lead', lookup(ch, 'summary'));
        const toc = add(article, 'div', 'learning-article-toc');
        add(toc, 'span', '', lang === 'id' ? 'Dalam bab ini:' : 'In this chapter:');
        ch.sections.forEach((section, i) => {
          const sectionWrap = add(article, 'section', 'learning-chapter-section');
          sectionWrap.id = section.id || (ch.key + '-s' + (i + 1));
          currentElements.push(sectionWrap);
          add(sectionWrap, 'h4', '', (section.number || (ch.number + '.' + (i + 1))) + '  ' + lookup(section, 'heading'));
          (section['paragraphs' + suffix] || []).forEach(p => add(sectionWrap, 'p', '', p));
          if ((section['bullets' + suffix] || []).length) {
            const ul = add(sectionWrap, 'ul', 'learning-bullets');
            section['bullets' + suffix].forEach(p => add(ul, 'li', '', p));
          }
          if ((section['frames' + suffix] || []).length)
            add(sectionWrap, 'pre', 'learning-frames', section['frames' + suffix].join('\n'));
          if (section.lab) {
            const exercise = section.lab;
            const panel = add(sectionWrap, 'div', 'learning-trace-lab');
            add(panel, 'span', 'learning-lab-label',
              lang === 'id' ? 'LATIHAN ANALISIS TRACE' : 'GUIDED TRACE LAB');
            add(panel, 'p', 'learning-lab-scenario', lookup(exercise, 'scenario'));
            if (exercise.wireHex) {
              const raw = add(panel, 'code', 'learning-lab-bytes', exercise.wireHex);
              raw.setAttribute('aria-label', lang === 'id' ? 'Byte frame mentah' : 'Raw frame bytes');
            }
            const steps = exercise['steps' + suffix] || [];
            if (steps.length) {
              const list = add(panel, 'ol', 'learning-lab-steps');
              steps.forEach(step => add(list, 'li', '', step));
            }
            add(panel, 'p', 'learning-lab-question', lookup(exercise, 'question'));
            const answer = add(panel, 'p', 'learning-lab-answer', lookup(exercise, 'answer'));
            answer.id = sectionWrap.id + '-answer';
            answer.hidden = true;
            const button = add(panel, 'button', 'learning-lab-reveal',
              lang === 'id' ? 'Tampilkan pembahasan' : 'Show explanation');
            button.type = 'button';
            button.setAttribute('aria-controls', answer.id);
            button.setAttribute('aria-expanded', 'false');
            button.addEventListener('click', () => {
              const reveal = answer.hidden;
              answer.hidden = !reveal;
              button.setAttribute('aria-expanded', reveal ? 'true' : 'false');
              button.textContent = reveal
                ? (lang === 'id' ? 'Sembunyikan pembahasan' : 'Hide explanation')
                : (lang === 'id' ? 'Tampilkan pembahasan' : 'Show explanation');
            });
          }
        });
        toc.appendChild(document.createTextNode(ch.sections.map((s, i) => s.number || (i + 1)).join('  ·  ')));
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
            } catch { /* Skip invalid source URLs. */ }
          });
        }
        const position = chapters.indexOf(ch);
        const pager = add(article, 'nav', 'learning-pager');
        pager.setAttribute('aria-label', lang === 'id' ? 'Navigasi bab' : 'Chapter navigation');
        for (const [offset, label] of [[-1, lang === 'id' ? '← Bab sebelumnya' : '← Previous chapter'],
                                       [1, lang === 'id' ? 'Bab berikutnya →' : 'Next chapter →']]) {
          const target = chapters[position + offset];
          if (!target) continue;
          const button = add(pager, 'button', 'learning-pager-button', label + ' · ' + lookup(target, 'title'));
          button.type = 'button';
          button.addEventListener('click', () => selectChapter(target));
        }
      };
      const renderMenu = () => {
        const terms = input.value.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
        menu.replaceChildren();
        let visible = 0;
        chapters.forEach(ch => {
          const sectionSearch = s => [s.headingEn, s.headingId,
            ...(s.paragraphsEn || []), ...(s.paragraphsId || []),
            ...(s.bulletsEn || []), ...(s.bulletsId || []),
            s.lab?.scenarioEn || '', s.lab?.scenarioId || '',
            s.lab?.questionEn || '', s.lab?.questionId || '',
            ...(s.lab?.stepsEn || []), ...(s.lab?.stepsId || [])].join(' ').toLocaleLowerCase();
          const chapterSearch = [ch.titleEn, ch.titleId, ch.summaryEn, ch.summaryId,
            ch.searchText].join(' ').toLocaleLowerCase();
          const searchable = [chapterSearch, ...ch.sections.map(sectionSearch)].join(' ');
          if (terms.length && !terms.every(term => searchable.includes(term))) return;
          visible += 1;
          const group = add(menu, 'div', 'learning-nav-group');
          const button = add(group, 'button', 'learning-chapter-button');
          button.type = 'button';
          const badge = add(button, 'span', 'learning-protocol-badge');
          const is101 = ch.key === 'learn-iec101' || ch.key === 'learn-lab-101';
          const is103 = ch.key === 'learn-iec103' || ch.key === 'learn-lab-103';
          const is104 = ch.key === 'learn-iec104' || ch.key === 'learn-lab-104';
          badge.textContent = is101 ? 'IEC101' : is103 ? 'IEC103' : is104 ? 'IEC104' : '·';
          if (is101) badge.classList.add('protocol-101');
          if (is103) badge.classList.add('protocol-103');
          if (is104) badge.classList.add('protocol-104');
          add(button, 'span', 'learning-chapter-name',
            (ch.number ? ch.number + ' ' : '') + lookup(ch, 'title'));
          button.setAttribute('aria-current', ch.key === current.key ? 'page' : 'false');
          button.setAttribute('aria-expanded', ch.key === current.key || !!terms.length ? 'true' : 'false');
          button.addEventListener('click', () => selectChapter(ch));
          if (ch.key === current.key || terms.length) {
            const sectionList = add(group, 'div', 'learning-section-list');
            ch.sections.forEach((section, i) => {
              if (terms.length && !terms.every(term => sectionSearch(section).includes(term)) &&
                  !terms.every(term => chapterSearch.includes(term))) return;
              const sectionButton = add(sectionList, 'button', 'learning-section-button');
              sectionButton.type = 'button';
              sectionButton.setAttribute('aria-current',
                ch.key === current.key && i === currentSection ? 'location' : 'false');
              add(sectionButton, 'span', 'learning-section-dot', '•');
              add(sectionButton, 'span', 'learning-section-label',
                (section.number || (i + 1)) + '  ' + lookup(section, 'heading'));
              sectionButton.addEventListener('click', () => selectChapter(ch, i));
            });
          }
        });
        if (!visible) add(menu, 'p', 'learning-no-result', labels.empty);
      };
      renderArticle(current);
      renderMenu();
      if (currentSection >= 0 && currentElements[currentSection])
        currentElements[currentSection].scrollIntoView({ behavior: 'auto', block: 'start' });
      input.addEventListener('input', renderMenu);
      window.addEventListener('hashchange', () => {
        selected = hashSelection();
        selectChapter(selected.chapter, selected.section, false);
      });
    })
    .catch(() => {
      nav.replaceChildren();
      article.replaceChildren();
      add(article, 'p', 'learning-load-error', labels.failed);
    });
})();
