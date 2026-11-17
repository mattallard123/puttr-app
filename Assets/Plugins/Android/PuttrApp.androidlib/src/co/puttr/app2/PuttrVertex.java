// The Vertex putter sensor on Android: the same lines as Plugins/Apple/PuttrVertex.m.
//
//   vstate \t n                  1 unsupported, 2 not allowed, 3 off, 4 on
//   vfound \t id \t name \t rssi \t manufacturer-hex
//   vconnected \t id \t name \t 1 if fff1 takes acknowledged writes
//   vdisconnected \t id
//   vstatus \t hex               fff1, read or notified
//   vdata \t hex                 fff2
//   verror \t text
package co.puttr.app2;

import android.annotation.SuppressLint;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanResult;
import android.bluetooth.le.ScanSettings;
import android.content.Context;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.util.SparseArray;

import java.util.ArrayDeque;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

@SuppressLint("MissingPermission")
public final class PuttrVertex {
    static UUID uuid(String head) { return UUID.fromString(head + "-0904-4eb4-8c23-a5f94f5034ee"); }
    static final UUID SERVICE = uuid("0000fff0"), CONTROL = uuid("0000fff1"), DATA = uuid("0000fff2"), RAW = uuid("0000fff3");
    static final UUID CCCD = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");

    static final ArrayDeque<String> events = new ArrayDeque<>();
    static final Handler main = new Handler(Looper.getMainLooper());
    static final Map<String, BluetoothDevice> seen = new HashMap<>();
    static final ArrayDeque<Runnable> ops = new ArrayDeque<>();
    static boolean busy, scanning;
    static Context context;
    static BluetoothAdapter adapter;
    static BluetoothGatt gatt;
    static BluetoothGattCharacteristic control;

    static void push(String line) {
        synchronized (events) {
            if (events.size() > 2000) events.pollFirst();
            events.addLast(line);
        }
    }

    public static String poll() { synchronized (events) { return events.pollFirst(); } }

    static String hex(byte[] b) {
        if (b == null) return "";
        StringBuilder s = new StringBuilder();
        for (byte x : b) s.append(String.format("%02x", x & 0xFF));
        return s.toString();
    }

    static String clean(String t) { return t == null ? "" : t.replace('\t', ' ').replace('\n', ' '); }

    static boolean ready(Context c) {
        context = c.getApplicationContext();
        BluetoothManager m = (BluetoothManager) context.getSystemService(Context.BLUETOOTH_SERVICE);
        adapter = m == null ? null : m.getAdapter();
        if (adapter == null) { push("vstate\t1"); return false; }
        if (!adapter.isEnabled()) { push("vstate\t3"); return false; }
        push("vstate\t4");
        return true;
    }

    public static void scan(Context c, boolean on) {
        main.post(() -> {
            if (!ready(c)) return;
            BluetoothLeScanner scanner = adapter.getBluetoothLeScanner();
            if (scanner == null) return;
            try {
                if (on && !scanning) { scanner.startScan(null, new ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build(), found); scanning = true; }
                else if (!on && scanning) { scanner.stopScan(found); scanning = false; }
            } catch (SecurityException e) { push("vstate\t2"); }
        });
    }

    static final ScanCallback found = new ScanCallback() {
        @Override public void onScanResult(int type, ScanResult r) {
            String name = r.getScanRecord() != null ? r.getScanRecord().getDeviceName() : null;
            if (name == null) name = r.getDevice().getName();
            if (name == null) return;
            String lower = name.toLowerCase();
            if (!(lower.startsWith("vtx") || lower.startsWith("vertex"))) return;
            String id = r.getDevice().getAddress();
            seen.put(id, r.getDevice());
            String maker = "";
            if (r.getScanRecord() != null) {
                SparseArray<byte[]> m = r.getScanRecord().getManufacturerSpecificData();
                if (m != null && m.size() > 0) {
                    // iOS hands over the company id (2 bytes, little-endian) and then the data; so do we.
                    int company = m.keyAt(0);
                    maker = String.format("%02x%02x", company & 0xFF, (company >> 8) & 0xFF) + hex(m.valueAt(0));
                }
            }
            push("vfound\t" + id + "\t" + clean(name) + "\t" + r.getRssi() + "\t" + maker);
        }
    };

    public static void connect(Context c, String id) {
        main.post(() -> {
            if (!ready(c)) return;
            if (scanning) scan(c, false);
            BluetoothDevice d = seen.get(id);
            if (d == null) try { d = adapter.getRemoteDevice(id); } catch (Exception ignored) { }
            if (d == null) { push("verror\tThat sensor is not in range"); return; }
            if (gatt != null) { gatt.close(); gatt = null; }
            ops.clear(); busy = false; control = null;
            gatt = d.connectGatt(context, false, callback, BluetoothDevice.TRANSPORT_LE);
        });
    }

    public static void disconnect() { main.post(() -> { if (gatt != null) gatt.disconnect(); }); }

    public static void readStatus() { op(() -> { if (control == null || !gatt.readCharacteristic(control)) done(); }); }

    public static void write(byte[] bytes) {
        op(() -> {
            if (control == null) { push("verror\tNot connected"); done(); return; }
            int type = (control.getProperties() & BluetoothGattCharacteristic.PROPERTY_WRITE) != 0 ? BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT : BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE;
            boolean ok;
            if (Build.VERSION.SDK_INT >= 33) ok = gatt.writeCharacteristic(control, bytes, type) == BluetoothGatt.GATT_SUCCESS;
            else { control.setWriteType(type); control.setValue(bytes); ok = gatt.writeCharacteristic(control); }
            if (!ok) { push("verror\tThe sensor did not take the command"); done(); }
            else if (type == BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE) done();
        });
    }

    static void op(Runnable r) { main.post(() -> { ops.addLast(r); next(); }); }
    static void next() { if (busy || gatt == null) return; Runnable r = ops.pollFirst(); if (r == null) return; busy = true; r.run(); }
    static void done() { main.post(() -> { busy = false; next(); }); }

    static boolean subscribe(BluetoothGattCharacteristic c) {
        if (c == null) return false;
        gatt.setCharacteristicNotification(c, true);
        BluetoothGattDescriptor d = c.getDescriptor(CCCD);
        if (d == null) return false;
        if (Build.VERSION.SDK_INT >= 33) return gatt.writeDescriptor(d, BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE) == BluetoothGatt.GATT_SUCCESS;
        d.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
        return gatt.writeDescriptor(d);
    }

    static void value(BluetoothGattCharacteristic c, byte[] v) {
        if (c.getUuid().equals(CONTROL)) push("vstatus\t" + hex(v));
        else if (c.getUuid().equals(DATA)) push("vdata\t" + hex(v));
    }

    static final BluetoothGattCallback callback = new BluetoothGattCallback() {
        @Override public void onConnectionStateChange(BluetoothGatt g, int status, int state) {
            if (state == BluetoothProfile.STATE_CONNECTED) main.post(() -> g.requestMtu(182));
            else if (state == BluetoothProfile.STATE_DISCONNECTED) {
                String id = g.getDevice().getAddress();
                main.post(() -> {
                    if (gatt == g) { gatt.close(); gatt = null; control = null; }
                    ops.clear(); busy = false;
                    push("vdisconnected\t" + id);
                });
            }
        }

        @Override public void onMtuChanged(BluetoothGatt g, int mtu, int status) { main.post(g::discoverServices); }

        @Override public void onServicesDiscovered(BluetoothGatt g, int status) {
            main.post(() -> {
                BluetoothGattService s = g.getService(SERVICE);
                if (s == null) { push("verror\tThis is not a Vertex sensor"); return; }
                control = s.getCharacteristic(CONTROL);
                BluetoothGattCharacteristic data = s.getCharacteristic(DATA), raw = s.getCharacteristic(RAW);
                if (control == null || data == null) { push("verror\tThe sensor's putt data was not found"); return; }
                op(() -> { if (!g.readCharacteristic(control)) done(); });
                op(() -> { if (!subscribe(control)) done(); });
                op(() -> { if (!subscribe(data)) { push("verror\tCould not subscribe to putt data"); done(); } else { } });
                if (raw != null) op(() -> { if (!subscribe(raw)) done(); });
                boolean acked = (control.getProperties() & BluetoothGattCharacteristic.PROPERTY_WRITE) != 0;
                String name = g.getDevice().getName();
                op(() -> { push("vconnected\t" + g.getDevice().getAddress() + "\t" + clean(name == null ? "Vertex" : name) + "\t" + (acked ? 1 : 0)); done(); });
            });
        }

        @Override public void onCharacteristicRead(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] v, int status) {
            if (status == BluetoothGatt.GATT_SUCCESS) value(c, v);
            done();
        }

        @SuppressWarnings("deprecation")
        @Override public void onCharacteristicRead(BluetoothGatt g, BluetoothGattCharacteristic c, int status) {
            if (Build.VERSION.SDK_INT >= 33) return;
            if (status == BluetoothGatt.GATT_SUCCESS) value(c, c.getValue());
            done();
        }

        @Override public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] v) { value(c, v); }

        @SuppressWarnings("deprecation")
        @Override public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) {
            if (Build.VERSION.SDK_INT >= 33) return;
            value(c, c.getValue());
        }

        @Override public void onCharacteristicWrite(BluetoothGatt g, BluetoothGattCharacteristic c, int status) { done(); }
        @Override public void onDescriptorWrite(BluetoothGatt g, BluetoothGattDescriptor d, int status) { done(); }
    };
}
