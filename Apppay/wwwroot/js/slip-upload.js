(function () {
    var input = document.getElementById('slipFiles');
    if (!input) return;

    var preview = document.getElementById('slipPreview');
    var status = document.getElementById('scanStatus');
    var amountInput = document.querySelector('[name="Amount"]');

    input.addEventListener('change', async function (e) {
        var files = e.target.files;

        if (preview) {
            preview.innerHTML = '';
            for (var i = 0; i < files.length; i++) {
                var img = document.createElement('img');
                img.src = URL.createObjectURL(files[i]);
                img.style.cssText = 'width:64px;height:64px;object-fit:cover;border-radius:8px;border:1px solid #dee2e6;';
                preview.appendChild(img);
            }
        }

        if (files.length === 0 || !amountInput) return;

        var hasAmount = amountInput.value && parseFloat(amountInput.value) > 0;
        if (hasAmount) return;

        var tokenInput = input.closest('form')?.querySelector('input[name="__RequestVerificationToken"]');
        var fd = new FormData();
        fd.append('file', files[0]);
        if (tokenInput) fd.append('__RequestVerificationToken', tokenInput.value);

        if (status) status.textContent = 'กำลังอ่านจำนวนเงินจากสลิป...';

        try {
            var res = await fetch('/Transactions/ScanSlip', { method: 'POST', body: fd });
            var data = await res.json();
            if (data.amount) {
                amountInput.value = data.amount;
                if (status) status.textContent = 'อ่านได้ ' + data.amount + ' บาท — กรุณาตรวจสอบก่อนบันทึก';
            } else if (status) {
                status.textContent = 'อ่านจำนวนเงินจากสลิปไม่สำเร็จ กรุณากรอกเอง';
            }
        } catch (err) {
            if (status) status.textContent = '';
        }
    });
})();
