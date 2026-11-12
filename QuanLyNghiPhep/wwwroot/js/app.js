window.leaveApp = {
    downloadBase64: function (filename, base64, contentType) {
        const raw = atob(base64);
        const bytes = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
        const blob = new Blob([bytes], { type: contentType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1500);
    }
};

window.leaveApp.submitLogout = () => document.getElementById("logout-form").requestSubmit();

window.leaveApp.scrollToId = (id) => {
    setTimeout(() => {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: "smooth", block: "center" });
    }, 50);
};

// Chỉ đổi kiểu input; không ghi mật khẩu vào storage hoặc URL.
window.leaveApp.togglePassword = button => {
    const input = button.parentElement.querySelector("input");
    const show = input.type === "password";
    input.type = show ? "text" : "password";
    button.setAttribute("aria-pressed", String(show));
    button.setAttribute("aria-label", show ? "Ẩn mật khẩu" : "Hiện mật khẩu");
    button.title = show ? "Ẩn mật khẩu" : "Hiện mật khẩu";
};

// Không lưu tài khoản trong trang. Xóa cả giá trị được khôi phục khi quay lại
// bằng Back/Forward; không dùng bộ hẹn giờ để tránh xóa nội dung người dùng gõ.
window.leaveApp.resetLoginForm = () => {
    const form = document.getElementById("login-form");
    if (!form) return;
    for (const name of ["username", "password"]) {
        const input = form.elements.namedItem(name);
        input.value = "";
        input.readOnly = true; // Mở nhập ngay khi người dùng click hoặc Tab vào ô.
    }
    const password = form.elements.namedItem("password");
    password.type = "password";
    const eye = password.parentElement.querySelector("button");
    eye.setAttribute("aria-pressed", "false");
    eye.setAttribute("aria-label", "Hiện mật khẩu");
    eye.title = "Hiện mật khẩu";
    delete form.dataset.userStarted;
};
document.addEventListener("focusin", event => {
    const input = event.target;
    if (input.form?.id !== "login-form" || !["username", "password"].includes(input.name)) return;
    input.readOnly = false;
    input.form.dataset.userStarted = "1";
});
window.leaveApp.resetLoginForm();
window.addEventListener("pageshow", event => {
    const form = document.getElementById("login-form");
    if (form && (event.persisted || form.dataset.userStarted !== "1")) window.leaveApp.resetLoginForm();
});

// Sai thông tin: giữ nguyên form/tên đã nhập, không tải trang để autofill tài khoản khác.
document.addEventListener("submit", async event => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || form.id !== "login-form") return;
    event.preventDefault();
    if (form.dataset.submitting === "1") return;
    form.dataset.submitting = "1";
    const button = form.querySelector('[type="submit"]');
    const error = document.getElementById("login-client-error");
    const username = form.elements.namedItem("username").value;
    const password = form.elements.namedItem("password");
    button.disabled = true; error.hidden = true;
    try {
        const response = await fetch(form.action, { method: "POST", body: new FormData(form),
            credentials: "same-origin", headers: { "X-Login-Form": "1" } });
        if (response.status === 429) throw new Error("Bạn đã thử quá nhiều lần. Vui lòng đợi một phút rồi thử lại.");
        if (!response.ok) throw new Error("Phiên biểu mẫu hết hạn hoặc kết nối gặp lỗi. Hãy tải lại trang rồi thử lại.");
        const result = await response.json();
        if (result.ok) { window.location.assign("/"); return; }
        throw new Error(result.message);
    } catch (failure) {
        form.elements.namedItem("username").value = username;
        password.value = "";
        password.type = "password";
        const eye = password.parentElement.querySelector("button");
        eye.setAttribute("aria-pressed", "false");
        eye.setAttribute("aria-label", "Hiện mật khẩu"); eye.title = "Hiện mật khẩu";
        error.textContent = failure.message || "Không thể kết nối. Vui lòng thử lại.";
        error.hidden = false;
        password.focus();
    } finally { delete form.dataset.submitting; button.disabled = false; }
});
