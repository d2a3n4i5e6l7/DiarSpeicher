using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace DiarSpeicher.Client.Interop;

/// <summary>The non-extractable WebCrypto key only exists in the browser; the module is embedded and imported as a data URI.</summary>
internal static partial class DpopJs
{
    private const string Module = "dpop";

    [JSImport("getPublicKeyBase64", Module)]
    public static partial Task<string> GetPublicKeyBase64();

    [JSImport("resetKeyPair", Module)]
    public static partial Task ResetKeyPair();

    [JSImport("signRequestProof", Module)]
    public static partial Task<string> SignRequestProof(string method, string path);

    public static Task Load() => JSHost.ImportAsync(Module, "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Source)));

    private const string Source = """"
        const DB_NAME = "gateway_crypto_db";
        const STORE_NAME = "keys";
        const KEY_NAME = "session_keypair";

        let cachedKeyPair = null;

        function openDb() {
            return new Promise((resolve, reject) => {
                if (!globalThis.indexedDB) {
                    reject(new Error("IndexedDB no está disponible en este entorno"));
                    return;
                }
                const request = globalThis.indexedDB.open(DB_NAME, 1);
                request.onupgradeneeded = () => {
                    const db = request.result;
                    if (!db.objectStoreNames.contains(STORE_NAME)) {
                        db.createObjectStore(STORE_NAME);
                    }
                };
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(new Error(request.error?.message || "Error al abrir IndexedDB"));
            });
        }

        async function loadKeyPairFromDb() {
            try {
                const db = await openDb();
                return await new Promise((resolve, reject) => {
                    const request = db.transaction(STORE_NAME, "readonly").objectStore(STORE_NAME).get(KEY_NAME);
                    request.onsuccess = () => resolve(request.result ?? null);
                    request.onerror = () => reject(new Error(request.error?.message || "Error al leer clave de IndexedDB"));
                });
            } catch {
                return null;
            }
        }

        async function saveKeyPairToDb(keyPair) {
            try {
                const db = await openDb();
                await new Promise((resolve, reject) => {
                    const request = db.transaction(STORE_NAME, "readwrite").objectStore(STORE_NAME).put(keyPair, KEY_NAME);
                    request.onsuccess = () => resolve();
                    request.onerror = () => reject(new Error(request.error?.message || "Error al guardar clave en IndexedDB"));
                });
            } catch {
                cachedKeyPair = keyPair;
            }
        }

        async function getOrCreateKeyPair() {
            if (cachedKeyPair) {
                return cachedKeyPair;
            }
            const stored = await loadKeyPairFromDb();
            if (stored?.privateKey && stored?.publicKey) {
                cachedKeyPair = stored;
                return stored;
            }
            const generated = await globalThis.crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
            cachedKeyPair = generated;
            await saveKeyPairToDb(generated);
            return generated;
        }

        function base64(bytes) {
            let binary = "";
            for (const byte of bytes) binary += String.fromCodePoint(byte);
            return globalThis.btoa(binary);
        }

        function base64Url(bytes) {
            return base64(bytes).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
        }

        function encodeSegment(value) {
            return base64Url(new TextEncoder().encode(JSON.stringify(value)));
        }

        export async function getPublicKeyBase64() {
            const keyPair = await getOrCreateKeyPair();
            return base64(new Uint8Array(await globalThis.crypto.subtle.exportKey("spki", keyPair.publicKey)));
        }

        export async function resetKeyPair() {
            cachedKeyPair = null;
            try {
                const db = await openDb();
                await new Promise((resolve) => {
                    const tx = db.transaction(STORE_NAME, "readwrite");
                    tx.objectStore(STORE_NAME).delete(KEY_NAME);
                    tx.oncomplete = () => resolve();
                    tx.onerror = () => resolve();
                    tx.onabort = () => resolve();
                });
            } catch {
                cachedKeyPair = null;
            }
        }

        // The gateway compares x and y with the key registered at login, so no extra JWK fields (ext, key_ops).
        async function publicJwk(publicKey) {
            const exported = await globalThis.crypto.subtle.exportKey("jwk", publicKey);
            return { crv: "P-256", kty: "EC", x: exported.x ?? "", y: exported.y ?? "" };
        }

        export async function signRequestProof(method, path) {
            const keyPair = await getOrCreateKeyPair();
            const jose = { typ: "dpop+jwt", alg: "ES256", jwk: await publicJwk(keyPair.publicKey) };
            const claims = {
                jti: globalThis.crypto.randomUUID(),
                htm: (method || "GET").toUpperCase(),
                htu: new URL(path.split("?")[0], globalThis.location.origin).href,
                iat: Math.floor(Date.now() / 1000),
            };
            const signingInput = `${encodeSegment(jose)}.${encodeSegment(claims)}`;
            const signature = await globalThis.crypto.subtle.sign({ name: "ECDSA", hash: { name: "SHA-256" } }, keyPair.privateKey, new TextEncoder().encode(signingInput));
            return `${signingInput}.${base64Url(new Uint8Array(signature))}`;
        }
        """";
}
