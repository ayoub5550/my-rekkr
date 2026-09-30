// my-rekkr dev7 — save backup through the Android document picker (Storage Access Framework): no storage
// permission, works on API 24+, the user chooses where the file lives (Downloads, Drive, SD card...), so it
// survives an uninstall. A tiny transparent activity starts ACTION_CREATE_DOCUMENT / ACTION_OPEN_DOCUMENT,
// copies the bytes between the chosen document and an app-private file, and reports back to Unity with
// UnitySendMessage("REKKR", "OnBackupResult", "<mode>:<ok|cancel|error>:<detail>").
// SPDX-License-Identifier: GPL-2.0-or-later
package com.ayoub.rekkr;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import com.unity3d.player.UnityPlayer;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

public class RekkrDocs extends Activity {
    private static final int REQ = 7301;
    private String mode, path;

    /** mode "export": copies file {@code path} to a new document named {@code name}; "import": copies a picked document to {@code path}. */
    public static void start(Activity from, String mode, String path, String name) {
        Intent i = new Intent(from, RekkrDocs.class);
        i.putExtra("mode", mode);
        i.putExtra("path", path);
        i.putExtra("name", name);
        from.startActivity(i);
    }

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        mode = getIntent().getStringExtra("mode");
        path = getIntent().getStringExtra("path");
        if (state != null) return;   // recreated while the picker is open: wait for the result
        Intent i;
        if ("export".equals(mode)) {
            i = new Intent(Intent.ACTION_CREATE_DOCUMENT);
            i.putExtra(Intent.EXTRA_TITLE, getIntent().getStringExtra("name"));
        } else {
            i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        }
        i.addCategory(Intent.CATEGORY_OPENABLE);
        i.setType("*/*");
        try {
            startActivityForResult(i, REQ);
        } catch (Exception e) {
            report("error", "no document picker: " + e.getMessage());
            finish();
        }
    }

    @Override
    protected void onActivityResult(int req, int result, Intent data) {
        super.onActivityResult(req, result, data);
        if (req != REQ) return;
        if (result != RESULT_OK || data == null || data.getData() == null) {
            report("cancel", "");
            finish();
            return;
        }
        Uri uri = data.getData();
        try {
            if ("export".equals(mode)) {
                try (InputStream in = new FileInputStream(path); OutputStream out = getContentResolver().openOutputStream(uri, "wt")) {
                    copy(in, out);
                }
            } else {
                try (InputStream in = getContentResolver().openInputStream(uri); OutputStream out = new FileOutputStream(path)) {
                    copy(in, out);
                }
            }
            report("ok", uri.getLastPathSegment() == null ? "" : uri.getLastPathSegment());
        } catch (Exception e) {
            report("error", String.valueOf(e.getMessage()));
        }
        finish();
    }

    private static void copy(InputStream in, OutputStream out) throws java.io.IOException {
        if (in == null || out == null) throw new java.io.IOException("cannot open the document");
        byte[] buf = new byte[65536];
        int n;
        while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        out.flush();
    }

    private void report(String status, String detail) {
        try {
            UnityPlayer.UnitySendMessage("REKKR", "OnBackupResult", mode + ":" + status + ":" + detail.replace(':', ' '));
        } catch (Throwable t) { /* Unity not running */ }
    }
}
