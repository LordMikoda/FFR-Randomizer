// FFR Randomizer art: the crystal mark, drawn from scratch (no game assets).
// Used by icon.html and banner.html; tools/make_art.py renders them.
// Original faceted crystal icon, drawn from scratch. Returns SVG markup.
// opts: {arrows: bool, bg: bool}
function crystalSVG(opts) {
  const P = {
    T: [262, 52], B: [248, 468],
    L1: [178, 168], L2: [168, 326], R1: [340, 160], R2: [346, 338],
    C1: [236, 150], C2: [228, 334], D1: [296, 146], D2: [300, 336],
    // inner refraction points on the front faces
    M: [262, 238], N: [244, 286], Q: [282, 210], K: [270, 392]
  };
  const pts = (...k) => k.map(n => P[n].join(',')).join(' ');
  const face = (k, fill, op = 1) => `<polygon points="${pts(...k)}" fill="${fill}" opacity="${op}"/>`;
  const g = (id, a, b, x1 = 0, y1 = 0, x2 = 1, y2 = 1) =>
    `<linearGradient id="${id}" x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}"><stop offset="0" stop-color="${a}"/><stop offset="1" stop-color="${b}"/></linearGradient>`;

  const defs = `<defs>
    ${g('bg', '#4a68cf', '#0a1356')}
    ${g('tl', '#d9f6ff', '#6fb8ff')}
    ${g('tf', '#bfe6ff', '#4f7dff', 0, 0, 0.6, 1)}
    ${g('tr', '#6f8cff', '#2a33b8')}
    ${g('ml', '#7cc8ff', '#2f55e0', 0, 0, 0.4, 1)}
    ${g('mf', '#4c7cff', '#1f2fb0', 0, 0, 0.3, 1)}
    ${g('mr', '#2f3fc9', '#140f7a', 0, 0, 0.2, 1)}
    ${g('bl', '#3a64ec', '#1a2299')}
    ${g('bf', '#2c46d8', '#120e78')}
    ${g('br', '#1c1e9e', '#0b0850')}
    ${g('ar', '#9fe9ff', '#3fa8ff')}
    <radialGradient id="glow" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#7fd4ff" stop-opacity=".55"/><stop offset="1" stop-color="#7fd4ff" stop-opacity="0"/></radialGradient>
    <clipPath id="body"><polygon points="${pts('T','R1','R2','B','L2','L1')}"/></clipPath>
  </defs>`;

  const bg = opts.bg ? `
    <rect x="12" y="12" width="488" height="488" rx="72" fill="url(#bg)" stroke="#e3e6ef" stroke-width="9"/>
    <rect x="20" y="20" width="472" height="472" rx="64" fill="none" stroke="#5c6488" stroke-width="4"/>` : '';

  const arrows = opts.arrows ? `
    <g fill="none" stroke="url(#ar)" stroke-width="26" stroke-linecap="round">
      <path d="M96 300 A168 168 0 0 1 170 112"/>
      <path d="M416 212 A168 168 0 0 1 342 400"/>
    </g>
    <polygon points="146,84 214,112 158,154" fill="#9fe9ff"/>
    <polygon points="366,428 298,400 354,358" fill="#3fa8ff"/>` : '';

  const crystal = `
    <ellipse cx="258" cy="262" rx="150" ry="215" fill="url(#glow)"/>
    <g>
      ${face(['T','L1','C1'], 'url(#tl)')}
      ${face(['T','C1','D1'], 'url(#tf)')}
      ${face(['T','D1','R1'], 'url(#tr)')}
      ${face(['L1','L2','C2','C1'], 'url(#ml)')}
      ${face(['C1','C2','D2','D1'], 'url(#mf)')}
      ${face(['D1','D2','R2','R1'], 'url(#mr)')}
      ${face(['L2','B','C2'], 'url(#bl)')}
      ${face(['C2','B','D2'], 'url(#bf)')}
      ${face(['D2','B','R2'], 'url(#br)')}
      <g clip-path="url(#body)">
        ${face(['C1','M','Q'], '#e9fbff', .55)}
        ${face(['C1','N','M'], '#9fdcff', .45)}
        ${face(['M','N','K'], '#5ad0ff', .35)}
        ${face(['Q','M','D2'], '#3a2fbf', .35)}
        ${face(['N','C2','K'], '#7a9cff', .3)}
        ${face(['L1','C1','N'], '#ffffff', .18)}
        ${face(['D1','Q','R1'], '#8fa6ff', .25)}
        <polygon points="200,200 236,150 226,262" fill="#7ff3ff" opacity=".35"/>
        <polygon points="262,238 300,270 286,322" fill="#6fe6ff" opacity=".45"/>
        <polygon points="244,286 262,238 286,322 270,392" fill="#2a1fa8" opacity=".35"/>
        <polygon points="196,332 228,334 214,410" fill="#59d8ff" opacity=".4"/>
        <polygon points="300,336 346,338 318,250" fill="#5a3fd6" opacity=".35"/>
        <polygon points="300,180 330,230 312,268" fill="#b8f6ff" opacity=".35"/>
        <polygon points="248,420 270,392 262,450" fill="#7ff3ff" opacity=".35"/>
        <polygon points="210,240 232,226 220,300" fill="#ffffff" opacity=".22"/>
      </g>
      <polygon points="${pts('T','R1','R2','B','L2','L1')}" fill="none" stroke="#0a0d40" stroke-width="5" stroke-linejoin="round"/>
      <g fill="none" stroke="#e6f7ff" stroke-width="2.5" opacity=".55" stroke-linejoin="round">
        <polyline points="${pts('L1','C1','D1','R1')}"/>
        <polyline points="${pts('L2','C2','D2','R2')}"/>
      </g>
      <polyline points="${pts('C1','C2')}" fill="none" stroke="#e6f7ff" stroke-width="2.5" opacity=".5"/>
      <polyline points="${pts('D1','D2')}" fill="none" stroke="#0a0d40" stroke-width="2.5" opacity=".5"/>
      <path d="M248 92 L256 74 L264 92 L282 100 L264 108 L256 126 L248 108 L230 100 Z" fill="#fff"/>
    </g>`;

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">${defs}${bg}${arrows}${crystal}</svg>`;
}
