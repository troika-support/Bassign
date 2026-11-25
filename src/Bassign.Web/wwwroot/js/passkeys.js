// Passkeys (WebAuthn) for Bassign. No framework: buttons are found by data attributes, so the click that
// starts the browser's passkey prompt is a real user gesture (some browsers insist on that).
//
//   <form data-passkey-form="signin|register"> … antiforgery token, <input name="credentialJson">,
//   <button type="button" data-passkey-start>, <p data-passkey-error hidden>
(function () {
    'use strict';

    const supported = !!(window.PublicKeyCredential && navigator.credentials);

    const toBytes = (text) => {
        const base64 = text.replace(/-/g, '+').replace(/_/g, '/');
        const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
        return Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
    };
    const toText = (buffer) => {
        if (!buffer) return null;
        let binary = '';
        new Uint8Array(buffer).forEach((b) => { binary += String.fromCharCode(b); });
        return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    };

    // Done by hand (not PublicKeyCredential.toJSON / parse…FromJSON): older browsers lack them and some
    // password managers break them.
    function creationOptions(json) {
        const o = { ...json, challenge: toBytes(json.challenge), user: { ...json.user, id: toBytes(json.user.id) } };
        o.excludeCredentials = (json.excludeCredentials || []).map((c) => ({ ...c, id: toBytes(c.id) }));
        return o;
    }
    function requestOptions(json) {
        const o = { ...json, challenge: toBytes(json.challenge) };
        o.allowCredentials = (json.allowCredentials || []).map((c) => ({ ...c, id: toBytes(c.id) }));
        return o;
    }
    function credentialJson(credential) {
        const r = credential.response;
        const response = { clientDataJSON: toText(r.clientDataJSON) };
        if (r.attestationObject) {
            response.attestationObject = toText(r.attestationObject);
            response.transports = typeof r.getTransports === 'function' ? r.getTransports() : [];
        } else {
            response.authenticatorData = toText(r.authenticatorData);
            response.signature = toText(r.signature);
            response.userHandle = toText(r.userHandle);
        }
        return JSON.stringify({
            id: credential.id,
            rawId: toText(credential.rawId),
            type: credential.type,
            authenticatorAttachment: credential.authenticatorAttachment || null,
            clientExtensionResults: credential.getClientExtensionResults ? credential.getClientExtensionResults() : {},
            response
        });
    }

    async function fetchOptions(url, form) {
        const token = form.querySelector('input[name="__RequestVerificationToken"]');
        const reply = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { RequestVerificationToken: token ? token.value : '' }
        });
        if (reply.status === 409) throw new Error((await reply.json()).detail || 'limit');
        if (!reply.ok) throw new Error('unavailable');
        return reply.json();
    }

    function show(form, message) {
        const box = form.querySelector('[data-passkey-error]');
        if (!box) return;
        box.textContent = message || '';
        box.hidden = !message;
    }

    async function start(form, button) {
        const kind = form.getAttribute('data-passkey-form');
        show(form, '');
        button.disabled = true;
        try {
            let credential;
            if (kind === 'register') {
                const options = creationOptions(await fetchOptions('/account/passkey/creation-options', form));
                credential = await navigator.credentials.create({ publicKey: options });
            } else {
                const options = requestOptions(await fetchOptions('/account/passkey/request-options', form));
                credential = await navigator.credentials.get({ publicKey: options });
            }
            if (!credential) throw new Error('none');
            form.querySelector('input[name="credentialJson"]').value = credentialJson(credential);
            form.submit();
        } catch (error) {
            button.disabled = false;
            const name = error && error.name;
            if (name === 'NotAllowedError' || name === 'AbortError') {
                show(form, kind === 'register' ? 'Cancelled — no passkey was added.' : 'Cancelled. Try again, or sign in with your password.');
            } else if (name === 'InvalidStateError') {
                show(form, 'This device already has a passkey for your account.');
            } else if (error && error.message && error.message.indexOf('passkeys') > 0) {
                show(form, error.message);
            } else {
                show(form, kind === 'register' ? "Couldn't add a passkey on this device. Try again, or use another device."
                                               : "Couldn't use a passkey here. Sign in with your password instead.");
            }
        }
    }

    document.addEventListener('click', (event) => {
        const button = event.target.closest && event.target.closest('[data-passkey-start]');
        if (!button) return;
        const form = button.closest('[data-passkey-form]');
        if (!form) return;
        event.preventDefault();
        if (!supported) { show(form, "This browser doesn't support passkeys."); return; }
        start(form, button);
    });
})();
