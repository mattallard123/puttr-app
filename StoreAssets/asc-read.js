// Read-only: the Puttr app (co.puttr.ios) in App Store Connect — its versions per platform and the en-US listing text.
//   node StoreAssets/asc-read.js > StoreAssets/listing/current.json
const fs = require('fs'), os = require('os'), path = require('path'), crypto = require('crypto');
const KEY_ID = process.env.ASC_KEY_ID || '2LQ24P399R';
const ISSUER = process.env.ASC_ISSUER_ID || '046b6041-467f-4b0a-9593-fa8d5ded7758';
function token() {
  const key = fs.readFileSync(path.join(os.homedir(), '.appstoreconnect/private_keys', `AuthKey_${KEY_ID}.p8`));
  const b64 = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const now = Math.floor(Date.now() / 1000);
  const head = b64({ alg: 'ES256', kid: KEY_ID, typ: 'JWT' }), body = b64({ iss: ISSUER, iat: now, exp: now + 1100, aud: 'appstoreconnect-v1' });
  return `${head}.${body}.${crypto.sign('sha256', Buffer.from(`${head}.${body}`), { key, dsaEncoding: 'ieee-p1363' }).toString('base64url')}`;
}
async function get(url) {
  const r = await fetch(`https://api.appstoreconnect.apple.com${url}`, { headers: { Authorization: `Bearer ${token()}` } });
  if (!r.ok) throw new Error(`${url}: ${r.status} ${(await r.text()).slice(0, 300)}`);
  return r.json();
}
(async () => {
  const apps = await get('/v1/apps?filter[bundleId]=co.puttr.ios');
  const app = apps.data[0];
  const out = { appId: app.id, name: app.attributes.name, versions: [] };
  const info = await get(`/v1/apps/${app.id}/appInfos?include=appInfoLocalizations`);
  out.appInfo = (info.included || []).filter((x) => x.attributes.locale === 'en-US').map((x) => x.attributes);
  const vs = await get(`/v1/apps/${app.id}/appStoreVersions?limit=20`);
  for (const v of vs.data) {
    const locs = await get(`/v1/appStoreVersions/${v.id}/appStoreVersionLocalizations`);
    const en = locs.data.find((l) => l.attributes.locale === 'en-US');
    const shots = en ? await get(`/v1/appStoreVersionLocalizations/${en.id}/appScreenshotSets`) : { data: [] };
    out.versions.push({ id: v.id, platform: v.attributes.platform, version: v.attributes.versionString, state: v.attributes.appStoreState,
      listing: en && en.attributes, screenshotSets: shots.data.map((s) => s.attributes.screenshotDisplayType) });
  }
  console.log(JSON.stringify(out, null, 1));
})().catch((e) => { console.error(e.message); process.exit(1); });
