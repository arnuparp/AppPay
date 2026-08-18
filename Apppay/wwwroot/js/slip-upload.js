(function () {
    var input = document.getElementById('slipFiles');
    if (!input) return;

    var preview = document.getElementById('slipPreview');
    var status = document.getElementById('scanStatus');
    var amountInput = document.querySelector('[name="Amount"]');
    var form = input.closest('form');

    var pending = []; // { uid, file, url, amount, scanned }
    var uidSeq = 0;

    function rebuildInputFiles() {
        var dt = new DataTransfer();
        pending.forEach(function (p) { dt.items.add(p.file); });
        input.files = dt.files;
    }

    function currentTotal() {
        return pending.reduce(function (sum, p) { return sum + (p.amount || 0); }, 0);
    }

    function applyTotalToAmount() {
        if (!amountInput || pending.length === 0) return;
        var total = currentTotal();
        if (total > 0) amountInput.value = total.toFixed(2);
    }

    function removePending(uid) {
        var target = pending.find(function (p) { return p.uid === uid; });
        if (target) URL.revokeObjectURL(target.url);
        pending = pending.filter(function (p) { return p.uid !== uid; });
        rebuildInputFiles();
        renderPreview();
        applyTotalToAmount();
        updateStatus();
    }

    function renderPreview() {
        if (!preview) return;
        preview.innerHTML = '';
        pending.forEach(function (p) {
            var wrap = document.createElement('div');
            wrap.className = 'position-relative';

            var img = document.createElement('img');
            img.src = p.url;
            img.style.cssText = 'width:64px;height:64px;object-fit:cover;border-radius:8px;border:1px solid #dee2e6;';
            wrap.appendChild(img);

            var badge = document.createElement('span');
            badge.className = 'badge bg-dark position-absolute bottom-0 start-0';
            badge.style.fontSize = '10px';
            badge.textContent = p.amount != null ? p.amount.toFixed(2) : (p.scanned ? 'อ่านไม่ได้' : '...');
            wrap.appendChild(badge);

            var removeBtn = document.createElement('button');
            removeBtn.type = 'button';
            removeBtn.className = 'btn btn-sm btn-danger rounded-circle p-0 position-absolute top-0 end-0';
            removeBtn.style.cssText = 'width:20px;height:20px;line-height:1;transform:translate(30%,-30%);';
            removeBtn.title = 'ยกเลิกรูปนี้';
            removeBtn.innerHTML = '<i class="bi bi-x" style="font-size:12px;"></i>';
            removeBtn.addEventListener('click', function () { removePending(p.uid); });
            wrap.appendChild(removeBtn);

            preview.appendChild(wrap);
        });
    }

    function updateStatus() {
        if (!status) return;
        if (pending.length === 0) { status.textContent = ''; return; }
        var scanning = pending.some(function (p) { return !p.scanned; });
        if (scanning) {
            status.textContent = 'กำลังอ่านจำนวนเงินจาก ' + pending.length + ' รูป...';
            return;
        }
        var total = currentTotal();
        status.textContent = total > 0
            ? 'รวมจากสลิป ' + pending.length + ' รูป = ' + total.toFixed(2) + ' บาท — กรุณาตรวจสอบก่อนบันทึก'
            : 'อ่านจำนวนเงินจากสลิปไม่สำเร็จ กรุณากรอกเอง';
    }

    async function scanPending() {
        var toScan = pending.filter(function (p) { return !p.scanned; });
        if (toScan.length === 0) return;

        var fd = new FormData();
        var tokenInput = form ? form.querySelector('input[name="__RequestVerificationToken"]') : null;
        if (tokenInput) fd.append('__RequestVerificationToken', tokenInput.value);
        toScan.forEach(function (p) { fd.append('files', p.file); });

        updateStatus();

        try {
            var res = await fetch('/Transactions/ScanSlips', { method: 'POST', body: fd });
            var data = await res.json();
            (data.amounts || []).forEach(function (amount, idx) {
                var p = toScan[idx];
                if (!p) return;
                p.scanned = true;
                p.amount = amount;
            });
        } catch (err) {
            toScan.forEach(function (p) { p.scanned = true; p.amount = null; });
        }

        renderPreview();
        applyTotalToAmount();
        updateStatus();
    }

    input.addEventListener('change', function (e) {
        var files = Array.prototype.slice.call(e.target.files || []);
        files.forEach(function (file) {
            pending.push({ uid: ++uidSeq, file: file, url: URL.createObjectURL(file), amount: null, scanned: false });
        });
        rebuildInputFiles();
        renderPreview();
        updateStatus();
        scanPending();
    });
})();
