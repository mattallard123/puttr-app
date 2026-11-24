// Puttr (co.puttr.ios): uploads the store screenshots and app previews in out/ to App Store Connect with the API.
//
//   node StoreAssets/upload.js list        what each version already has
//   node StoreAssets/upload.js upload IOS|TV_OS     replace that platform's screenshots and previews (iPhone 6.9", iPad 13" / Apple TV)
//   node StoreAssets/upload.js listing IOS|TV_OS    listing.md -> that version's description, keywords, promo, URLs, what's new
//   node StoreAssets/upload.js creative    iOS 27 product page header + search results images (creative/build.py)
//
// Key: ~/.appstoreconnect/private_keys/AuthKey_<ASC_KEY_ID>.p8 (never in the repo). ASC_KEY_ID and
// ASC_ISSUER_ID override the defaults below. Only the en-US localization of the editable 1.0 versions is touched.
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');

const APP_ID = '1528616307'; // Puttr (PuttrCo, co.puttr.ios)
const KEY_ID = process.env.ASC_KEY_ID || '2LQ24P399R';
const ISSUER = process.env.ASC_ISSUER_ID || '046b6041-467f-4b0a-9593-fa8d5ded7758';
const LOCALE = 'en-US';
const OUT = path.join(__dirname, 'out');
const PLATFORMS = {
  IOS: { shots: [['APP_IPHONE_67', 'screenshots/iphone-6.9'], ['APP_IPAD_PRO_3GEN_129', 'screenshots/ipad-13']], previews: [['IPHONE_67', 'previews/iphone-preview-886x1920.mp4']] },
  TV_OS: { shots: [['APP_APPLE_TV', 'screenshots/appletv']], previews: [['APPLE_TV', 'previews/appletv-preview-1920x1080.mp4']] },
};

function token() {
  const key = fs.readFileSync(path.join(os.homedir(), '.appstoreconnect/private_keys', `AuthKey_${KEY_ID}.p8`));
  const b64 = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const now = Math.floor(Date.now() / 1000);
  const head = b64({ alg: 'ES256', kid: KEY_ID, typ: 'JWT' });
  const body = b64({ iss: ISSUER, iat: now, exp: now + 1100, aud: 'appstoreconnect-v1' });
  const sig = crypto.sign('sha256', Buffer.from(`${head}.${body}`), { key, dsaEncoding: 'ieee-p1363' }).toString('base64url');
  return `${head}.${body}.${sig}`;
}

async function api(method, url, body) {
  const res = await fetch(url.startsWith('http') ? url : `https://api.appstoreconnect.apple.com${url}`, {
    method, headers: { Authorization: `Bearer ${token()}`, 'Content-Type': 'application/json' }, body: body && JSON.stringify(body),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${url}: ${res.status} ${text.slice(0, 600)}`);
  return text ? JSON.parse(text) : null;
}

async function versionLocalization(platform) {
  const v = await api('GET', `/v1/apps/${APP_ID}/appStoreVersions?filter[platform]=${platform}&limit=10`);
  const editable = v.data.find((x) => ['PREPARE_FOR_SUBMISSION', 'DEVELOPER_REJECTED', 'REJECTED', 'METADATA_REJECTED'].includes(x.attributes.appStoreState));
  if (!editable) throw new Error(`no editable ${platform} version`);
  const locs = await api('GET', `/v1/appStoreVersions/${editable.id}/appStoreVersionLocalizations`);
  const loc = locs.data.find((l) => l.attributes.locale === LOCALE);
  if (!loc) throw new Error(`${platform} ${editable.attributes.versionString} has no ${LOCALE} localization`);
  return { version: editable, loc };
}

async function sets(loc, kind) {
  const r = await api('GET', `/v1/appStoreVersionLocalizations/${loc.id}/${kind === 'shot' ? 'appScreenshotSets' : 'appPreviewSets'}?include=${kind === 'shot' ? 'appScreenshots' : 'appPreviews'}`);
  return r;
}

async function ensureSet(loc, kind, type) {
  const r = await sets(loc, kind);
  const attr = kind === 'shot' ? 'screenshotDisplayType' : 'previewType';
  let set = r.data.find((s) => s.attributes[attr] === type);
  if (!set) {
    const res = kind === 'shot' ? 'appScreenshotSets' : 'appPreviewSets';
    set = (await api('POST', `/v1/${res}`, { data: { type: res, attributes: { [attr]: type }, relationships: { appStoreVersionLocalization: { data: { type: 'appStoreVersionLocalizations', id: loc.id } } } } })).data;
  }
  const rel = kind === 'shot' ? 'appScreenshots' : 'appPreviews';
  const items = (await api('GET', `/v1/${kind === 'shot' ? 'appScreenshotSets' : 'appPreviewSets'}/${set.id}/${rel}?limit=50`)).data;
  return { set, items };
}

async function uploadFile(kind, setId, file, mime) {
  const res = kind === 'shot' ? 'appScreenshots' : 'appPreviews';
  const setRel = kind === 'shot' ? { appScreenshotSet: { data: { type: 'appScreenshotSets', id: setId } } } : { appPreviewSet: { data: { type: 'appPreviewSets', id: setId } } };
  const bytes = fs.readFileSync(file);
  const attrs = { fileName: path.basename(file), fileSize: bytes.length };
  if (mime) attrs.mimeType = mime;
  const made = (await api('POST', `/v1/${res}`, { data: { type: res, attributes: attrs, relationships: setRel } })).data;
  for (const op of made.attributes.uploadOperations) {
    const headers = Object.fromEntries((op.requestHeaders || []).map((h) => [h.name, h.value]));
    const r = await fetch(op.url, { method: op.method, headers, body: bytes.subarray(op.offset, op.offset + op.length) });
    if (!r.ok) throw new Error(`upload part ${op.offset} of ${file}: ${r.status} ${(await r.text()).slice(0, 200)}`);
  }
  const md5 = crypto.createHash('md5').update(bytes).digest('hex');
  await api('PATCH', `/v1/${res}/${made.id}`, { data: { type: res, id: made.id, attributes: { uploaded: true, sourceFileChecksum: md5 } } });
  return made.id;
}

async function state(kind, id) {
  const r = await api('GET', `/v1/${kind === 'shot' ? 'appScreenshots' : 'appPreviews'}/${id}`);
  return r.data.attributes.assetDeliveryState;
}

// listing.md → the app's name/subtitle/privacy URL/categories and each version's description,
// keywords, promotional text and URLs. What's New is left alone: Apple refuses it on a first version.
function listing() {
  const md = fs.readFileSync(path.join(__dirname, 'listing', 'listing.md'), 'utf8');
  const sec = (title) => {
    const m = md.match(new RegExp(`^## ${title}[^\\n]*\\n([\\s\\S]*?)(?=^## |$(?![\\s\\S]))`, 'm'));
    if (!m) throw new Error(`listing.md has no "## ${title}"`);
    return m[1].trim();
  };
  const urls = sec('URLs');
  const url = (k) => urls.match(new RegExp(`${k}: (\\S+)`))[1];
  return {
    promotionalText: sec('Promotional text'),
    keywords: sec('Keywords'), description: sec('Description'), whatsNew: sec("What's New"),
    supportUrl: url('Support'), marketingUrl: url('Marketing'), privacyPolicyUrl: url('Privacy'),
  };
}

// Version text only (description, keywords, promotional text, URLs, What's New where allowed) for the platforms named.
// The app info (name, subtitle, categories) is shared by every platform and goes live with whichever version is
// approved next, so it is not written here.
async function writeListing(platforms) {
  const L = listing();
  if (L.promotionalText.length > 170 || Buffer.byteLength(L.keywords) > 100 || L.description.length > 4000) throw new Error('listing.md: a field is over its limit');
  for (const platform of platforms) {
    const { version, loc } = await versionLocalization(platform);
    const attributes = { description: L.description, keywords: L.keywords, promotionalText: L.promotionalText, supportUrl: L.supportUrl, marketingUrl: L.marketingUrl };
    if (version.attributes.versionString !== '1.0') attributes.whatsNew = L.whatsNew;
    await api('PATCH', `/v1/appStoreVersionLocalizations/${loc.id}`, { data: { type: 'appStoreVersionLocalizations', id: loc.id, attributes } });
    console.log(`${platform} ${version.attributes.versionString}: description ${L.description.length} chars, keywords, promo, URLs${attributes.whatsNew ? ", what's new" : ''}`);
  }
}

// iOS 27 creative assets: out/creative/header-3840x1646.png and search-3840x2560.png go into the App Asset
// Library (category CREATIVE_ASSETS), then onto the iOS version as its product page header and search result.
const CREATIVE = [
  { file: 'creative/header-3840x1646.png', placementType: 'PRODUCT_PAGE_HEADER_ASSET', name: 'Puttr 3.0 header: Your putter is the controller' },
  { file: 'creative/search-3840x2560.png', placementType: 'APP_STORE_SEARCH_RESULTS_ASSET', name: 'Puttr 3.0 search results: 40+ games on your mat' },
];
async function writeCreative() {
  const lib = (await api('GET', `/v1/apps/${APP_ID}/assetLibrary`)).data;
  const { version, loc } = await versionLocalization('IOS');
  for (const c of CREATIVE) {
    const file = path.join(OUT, c.file), bytes = fs.readFileSync(file);
    const img = (await api('POST', '/v1/appAssetLibraryImages', { data: { type: 'appAssetLibraryImages',
      attributes: { fileName: path.basename(file), fileSize: bytes.length, category: 'CREATIVE_ASSETS', referenceName: c.name },
      relationships: { assetLibrary: { data: { type: 'appAssetLibraries', id: lib.id } } } } })).data;
    for (const op of img.attributes.uploadOperations) {
      const headers = Object.fromEntries((op.requestHeaders || []).map((h) => [h.name, h.value]));
      const r = await fetch(op.url, { method: op.method, headers, body: bytes.subarray(op.offset, op.offset + op.length) });
      if (!r.ok) throw new Error(`upload ${c.file}: ${r.status}`);
    }
    await api('PATCH', `/v1/appAssetLibraryImages/${img.id}`, { data: { type: 'appAssetLibraryImages', id: img.id, attributes: { uploaded: true } } });
    let st;
    for (let i = 0; i < 60; i++) {
      st = (await api('GET', `/v1/appAssetLibraryImages/${img.id}`)).data.attributes;
      if (st.state !== 'UPLOAD_COMPLETE' && st.state !== 'AWAITING_UPLOAD') break;
      await new Promise((r) => setTimeout(r, 5000));
    }
    if (st.state !== 'PREPARE_FOR_SUBMISSION') throw new Error(`${c.file}: ${st.state} ${JSON.stringify(st.stateDetails)}`);
    console.log(`${c.file}: processed (spec ${st.specId}, ${st.imageAsset.width}x${st.imageAsset.height})`);
    const pl = (await api('POST', '/v1/appAssetLibraryPlacements', { data: { type: 'appAssetLibraryPlacements',
      attributes: { placementType: c.placementType, placementGroup: 'DEFAULT_PROFILE' },
      relationships: { image: { data: { type: 'appAssetLibraryImages', id: img.id } },
        appStoreVersionLocalization: { data: { type: 'appStoreVersionLocalizations', id: loc.id } } } } })).data;
    console.log(`  placed as ${c.placementType} on iOS ${version.attributes.versionString}: ${pl.attributes.state}`);
  }
}

module.exports = { api, APP_ID, versionLocalization };
if (require.main === module && process.argv[2] === 'listing') writeListing(process.argv.slice(3)).catch((e) => { console.error(e.message); process.exit(1); });
else if (require.main === module && process.argv[2] === 'creative') writeCreative().catch((e) => { console.error(e.message); process.exit(1); });
else
if (require.main === module) (async () => {
  const cmd = process.argv[2] || 'list';
  const only = process.argv.slice(3); // e.g. "upload TV_OS": just that platform
  for (const [platform, p] of Object.entries(PLATFORMS)) {
    if (only.length && !only.includes(platform)) continue;
    const { version, loc } = await versionLocalization(platform);
    console.log(`\n${platform} ${version.attributes.versionString} (${version.attributes.appStoreState}) ${LOCALE}`);
    const all = await sets(loc, 'shot');
    for (const s of all.data) console.log(`  screenshot set ${s.attributes.screenshotDisplayType}: ${(s.relationships.appScreenshots.data || []).length}`);
    const allp = await sets(loc, 'preview');
    for (const s of allp.data) console.log(`  preview set ${s.attributes.previewType}: ${(s.relationships.appPreviews.data || []).length}`);
    if (cmd !== 'upload') continue;

    const ids = [];
    for (const [type, dir] of p.shots) {
      const shots = await ensureSet(loc, 'shot', type);
      for (const old of shots.items) await api('DELETE', `/v1/appScreenshots/${old.id}`);
      const files = fs.readdirSync(path.join(OUT, dir)).filter((f) => f.endsWith('.png')).sort();
      const mine = [];
      for (const f of files) { const id = await uploadFile('shot', shots.set.id, path.join(OUT, dir, f)); mine.push(id); ids.push(['shot', id]); console.log(`  ${type} uploaded ${f}`); }
      await api('PATCH', `/v1/appScreenshotSets/${shots.set.id}/relationships/appScreenshots`, { data: mine.map((id) => ({ type: 'appScreenshots', id })) });
    }
    for (const [type, file] of p.previews) {
      const prev = await ensureSet(loc, 'preview', type);
      for (const old of prev.items) await api('DELETE', `/v1/appPreviews/${old.id}`);
      ids.push(['preview', await uploadFile('preview', prev.set.id, path.join(OUT, file), 'video/mp4')]);
      console.log(`  ${type} uploaded ${path.basename(file)}`);
    }

    // Wait for Apple to process them.
    for (let i = 0; i < 60; i++) {
      const states = await Promise.all(ids.map(([k, id]) => state(k, id)));
      const failed = states.filter((s) => s.state === 'FAILED');
      if (failed.length) { console.log('  FAILED', JSON.stringify(failed.map((s) => s.errors))); break; }
      const pending = states.filter((s) => s.state !== 'COMPLETE').length;
      if (!pending) { console.log(`  all ${ids.length} processed`); break; }
      if (i === 59) console.log(`  ${pending} still processing (the preview can take a while)`);
      await new Promise((r) => setTimeout(r, 10000));
    }
  }
})().catch((e) => { console.error(e.message); process.exit(1); });
