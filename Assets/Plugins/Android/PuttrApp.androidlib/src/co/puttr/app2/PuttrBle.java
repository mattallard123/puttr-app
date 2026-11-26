// A Bluetooth central for a Puttr console on Android: the same lines as Plugins/Apple/PuttrBle.m,
// so PuttrBluetooth.cs reads both the same way.
//
//   state \t n                 1 unsupported, 2 not allowed, 3 off, 4 scanning, 5 connecting, 6 connected
//   found \t id \t name \t rssi
//   connected \t id \t name
//   disconnected \t id
//   hole \t [x, y, diameter]
//   version \t 2.0
//   ball \t [[x,y,r],...]
//   value \t head \t text      a read or notification of any other characteristic (Wi-Fi setup, level)
//   wrote \t head \t ok|fail
//   error \t text
//
// Unity polls (poll() returns the next line or null). GATT calls go one at a time, as Android needs.
package co.puttr.app2;

import android.Manifest;
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
import android.bluetooth.le.ScanFilter;
import android.bluetooth.le.ScanResult;
import android.bluetooth.le.ScanSettings;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelUuid;

import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

@SuppressLint("MissingPermission")
public final class PuttrBle {
    static final String TAIL = "-1121-0219-0708-41D6CBBC36D1";
    static final UUID SERVICE = uuid("538AB800");
    static final UUID BALL = uuid("538AB801"), HOLE = uuid("538AB802"), VERSION = uuid("538AB804");
    static final UUID CCCD = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");

    static UUID uuid(String head) { return UUID.fromString(head + TAIL); }
    static String head(UUID u) { return u.toString().substring(0, 8).toUpperCase(); }

    static final ArrayDeque<String> events = new ArrayDeque<>();
    static final Handler main = new Handler(Looper.getMainLooper());
    static final Map<String, BluetoothDevice> seen = new HashMap<>();
    static final Map<String, Long> told = new HashMap<>();
    static final ArrayDeque<Runnable> gattQueue = new ArrayDeque<>();
    static boolean gattBusy;

    static Context context;
    static BluetoothAdapter adapter;
    static BluetoothLeScanner scanner;
    static BluetoothGatt gatt;
    static int state;
    static boolean wanted, scanning;

    static void push(String line) {
        synchronized (events) {
            if (events.size() > 4000) events.pollFirst();
            events.addLast(line);
        }
    }

    static void tell(int s) {
        if (state == s) return;
        state = s;
        push("state\t" + s);
    }

    static String clean(String t) { return t == null ? "" : t.replace('\t', ' ').replace('\n', ' '); }

    public static String poll() {
        synchronized (events) { return events.pollFirst(); }
    }

    static boolean allowed() {
        if (Build.VERSION.SDK_INT >= 31)
            return context.checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN) == PackageManager.PERMISSION_GRANTED
                && context.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED;
        return context.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED;
    }

    /** Begin scanning. Unity asks for the permissions first; call again once they are granted. */
    public static void start(Context c) {
        main.post(() -> {
            context = c.getApplicationContext();
            wanted = true;
            BluetoothManager m = (BluetoothManager) context.getSystemService(Context.BLUETOOTH_SERVICE);
            adapter = m == null ? null : m.getAdapter();
            if (adapter == null) { tell(1); return; }
            if (!allowed()) { tell(2); return; }
            if (!adapter.isEnabled()) { tell(3); main.postDelayed(() -> { if (wanted && gatt == null) start(c); }, 2000); return; }
            scan();
        });
    }

    public static void stop() {
        main.post(() -> {
            wanted = false;
            // The app marks the radio off itself when it stops, so the next start must tell the state again even where
            // it has not changed (it left the app showing Bluetooth as off, while scanning, until it was relaunched).
            state = 0;
            stopScan();
            if (gatt != null) { gatt.disconnect(); gatt.close(); gatt = null; }
        });
    }

    static void scan() {
        if (!wanted || scanning || adapter == null) return;
        scanner = adapter.getBluetoothLeScanner();
        if (scanner == null) { tell(3); return; }
        List<ScanFilter> filters = new ArrayList<>();
        filters.add(new ScanFilter.Builder().setServiceUuid(new ParcelUuid(SERVICE)).build());
        ScanSettings settings = new ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build();
        try { scanner.startScan(filters, settings, scanCallback); scanning = true; }
        catch (Exception e) { push("error\t" + clean(e.getMessage())); return; }
        if (gatt == null) tell(4);
    }

    static void stopScan() {
        if (scanning && scanner != null) try { scanner.stopScan(scanCallback); } catch (Exception ignored) { }
        scanning = false;
    }

    static final ScanCallback scanCallback = new ScanCallback() {
        @Override public void onScanResult(int type, ScanResult r) {
            BluetoothDevice d = r.getDevice();
            String id = d.getAddress();
            seen.put(id, d);
            long now = System.currentTimeMillis();
            Long last = told.get(id);
            if (last != null && now - last < 1000) return;
            told.put(id, now);
            String name = r.getScanRecord() != null ? r.getScanRecord().getDeviceName() : null;
            if (name == null || name.isEmpty()) name = d.getName();
            push("found\t" + id + "\t" + clean(name == null || name.isEmpty() ? "Puttr" : name) + "\t" + r.getRssi());
        }
        @Override public void onScanFailed(int code) { scanning = false; push("error\tScan failed (" + code + ")"); }
    };

    public static void connect(String id) {
        main.post(() -> {
            if (adapter == null || !allowed()) return;
            BluetoothDevice d = seen.get(id);
            if (d == null) try { d = adapter.getRemoteDevice(id); } catch (Exception ignored) { }
            if (d == null) { push("error\tThat console is not in range"); return; }
            if (gatt != null) { gatt.disconnect(); gatt.close(); gatt = null; }
            gattQueue.clear();
            gattBusy = false;
            tell(5);
            gatt = Build.VERSION.SDK_INT >= 23 ? d.connectGatt(context, false, gattCallback, BluetoothDevice.TRANSPORT_LE) : d.connectGatt(context, false, gattCallback);
        });
    }

    public static void disconnect() {
        main.post(() -> { if (gatt != null) gatt.disconnect(); });
    }

    // ── Any characteristic of the console's service, for Wi-Fi setup and the level ──

    public static void read(String head) {
        queue(() -> {
            BluetoothGattCharacteristic c = characteristic(uuid(head));
            if (c == null || !gatt.readCharacteristic(c)) { push("value\t" + head + "\t"); done(); }
        });
    }

    public static void write(String head, String text) {
        queue(() -> {
            BluetoothGattCharacteristic c = characteristic(uuid(head));
            byte[] bytes = text.getBytes(StandardCharsets.UTF_8);
            boolean ok = false;
            if (c != null) {
                int type = (c.getProperties() & BluetoothGattCharacteristic.PROPERTY_WRITE) != 0 ? BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT : BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE;
                if (Build.VERSION.SDK_INT >= 33) ok = gatt.writeCharacteristic(c, bytes, type) == BluetoothGatt.GATT_SUCCESS;
                else { c.setWriteType(type); c.setValue(bytes); ok = gatt.writeCharacteristic(c); }
            }
            if (!ok) { push("wrote\t" + head + "\tfail"); done(); }
        });
    }

    public static void subscribe(String head) {
        queue(() -> { if (!listen(characteristic(uuid(head)))) done(); });
    }

    static BluetoothGattCharacteristic characteristic(UUID u) {
        if (gatt == null) return null;
        BluetoothGattService s = gatt.getService(SERVICE);
        return s == null ? null : s.getCharacteristic(u);
    }

    static boolean listen(BluetoothGattCharacteristic c) {
        if (c == null || gatt == null) return false;
        gatt.setCharacteristicNotification(c, true);
        BluetoothGattDescriptor d = c.getDescriptor(CCCD);
        if (d == null) return false;
        if (Build.VERSION.SDK_INT >= 33) return gatt.writeDescriptor(d, BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE) == BluetoothGatt.GATT_SUCCESS;
        d.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
        return gatt.writeDescriptor(d);
    }

    static void queue(Runnable r) {
        main.post(() -> { gattQueue.addLast(r); next(); });
    }

    static void next() {
        if (gattBusy || gatt == null) return;
        Runnable r = gattQueue.pollFirst();
        if (r == null) return;
        gattBusy = true;
        r.run();
    }

    static void done() {
        main.post(() -> { gattBusy = false; next(); });
    }

    static void value(BluetoothGattCharacteristic c, byte[] bytes) {
        if (bytes == null) return;
        String text = clean(new String(bytes, StandardCharsets.UTF_8));
        UUID u = c.getUuid();
        if (u.equals(BALL)) push("ball\t" + text);
        else if (u.equals(HOLE)) push("hole\t" + text);
        else if (u.equals(VERSION)) push("version\t" + text);
        else push("value\t" + head(u) + "\t" + text);
    }

    static final BluetoothGattCallback gattCallback = new BluetoothGattCallback() {
        @Override public void onConnectionStateChange(BluetoothGatt g, int status, int newState) {
            if (newState == BluetoothProfile.STATE_CONNECTED) {
                main.post(() -> { g.requestMtu(517); });
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                String id = g.getDevice().getAddress();
                main.post(() -> {
                    if (status != BluetoothGatt.GATT_SUCCESS && state == 5) push("error\tCould not connect (" + status + ")");
                    push("disconnected\t" + id);
                    if (gatt == g) { gatt.close(); gatt = null; }
                    gattQueue.clear();
                    gattBusy = false;
                    state = 0;
                    tell(wanted ? 4 : 3);
                });
            }
        }

        @Override public void onMtuChanged(BluetoothGatt g, int mtu, int status) {
            main.post(g::discoverServices);
        }

        @Override public void onServicesDiscovered(BluetoothGatt g, int status) {
            main.post(() -> {
                BluetoothGattService s = g.getService(SERVICE);
                BluetoothGattCharacteristic ball = s == null ? null : s.getCharacteristic(BALL);
                if (ball == null) { push("error\tThis device has no ball path: is it a Puttr?"); return; }
                queue(() -> { if (!listen(ball)) done(); });
                for (UUID u : new UUID[] { HOLE, VERSION }) {
                    BluetoothGattCharacteristic c = s.getCharacteristic(u);
                    if (c != null) queue(() -> { if (!g.readCharacteristic(c)) done(); });
                }
                String name = g.getDevice().getName();
                push("connected\t" + g.getDevice().getAddress() + "\t" + clean(name == null || name.isEmpty() ? "Puttr" : name));
                tell(6);
            });
        }

        // Android 13 and later.
        @Override public void onCharacteristicRead(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] value, int status) {
            if (status == BluetoothGatt.GATT_SUCCESS) value(c, value);
            done();
        }

        @SuppressWarnings("deprecation")
        @Override public void onCharacteristicRead(BluetoothGatt g, BluetoothGattCharacteristic c, int status) {
            if (Build.VERSION.SDK_INT >= 33) return;
            if (status == BluetoothGatt.GATT_SUCCESS) value(c, c.getValue());
            done();
        }

        @Override public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] value) { value(c, value); }

        @SuppressWarnings("deprecation")
        @Override public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) {
            if (Build.VERSION.SDK_INT >= 33) return;
            value(c, c.getValue());
        }

        @Override public void onCharacteristicWrite(BluetoothGatt g, BluetoothGattCharacteristic c, int status) {
            push("wrote\t" + head(c.getUuid()) + "\t" + (status == BluetoothGatt.GATT_SUCCESS ? "ok" : "fail"));
            done();
        }

        @Override public void onDescriptorWrite(BluetoothGatt g, BluetoothGattDescriptor d, int status) { done(); }
    };
}
