// A Bluetooth central for a Puttr console, for Unity on macOS.
//
// The console is one GATT service (538ab800-...). Putts arrive as
// notifications on 538ab801, each one a complete JSON array of up to 12
// points; the hole is a plain read of 538ab802 and the firmware version a
// read of 538ab804. There is no pairing and nothing to write: subscribing is
// enough. (538ab805 never answers a read. It is left alone.)
//
// Unity polls. Everything that happens is queued as a line of text:
//
//   state \t n                 1 unsupported, 2 not allowed, 3 off, 4 scanning, 5 connecting, 6 connected
//   found \t id \t name \t rssi
//   connected \t id \t name
//   disconnected \t id
//   hole \t [x, y, diameter]
//   version \t 2.0
//   ball \t [[x,y,r],...]
//
// Build with build.sh in this folder; the result goes to Assets/Plugins/macOS.
#import <Foundation/Foundation.h>
#import <CoreBluetooth/CoreBluetooth.h>

static NSString *const kTail = @"-1121-0219-0708-41D6CBBC36D1";

static CBUUID *Uuid(NSString *head) { return [CBUUID UUIDWithString:[head stringByAppendingString:kTail]]; }

@interface PuttrBle : NSObject <CBCentralManagerDelegate, CBPeripheralDelegate>
@property (strong) CBCentralManager *central;
@property (strong) dispatch_queue_t queue;
@property (strong) NSMutableDictionary<NSString *, CBPeripheral *> *seen;
@property (strong) NSMutableDictionary<NSString *, NSDate *> *told;
@property (strong) NSMutableArray<NSString *> *events;
@property (strong) CBPeripheral *active;
@property (assign) int state;
@property (assign) BOOL wanted;
@end

@implementation PuttrBle

- (instancetype)init {
    if ((self = [super init])) {
        _seen = [NSMutableDictionary dictionary];
        _told = [NSMutableDictionary dictionary];
        _events = [NSMutableArray array];
        _queue = dispatch_queue_create("co.puttr.ble", DISPATCH_QUEUE_SERIAL);
    }
    return self;
}

- (void)push:(NSString *)line {
    @synchronized (self.events) {
        if (self.events.count > 4000) [self.events removeObjectAtIndex:0];
        [self.events addObject:line];
    }
}

- (void)setStateAndTell:(int)state {
    if (self.state == state) return;
    self.state = state;
    [self push:[NSString stringWithFormat:@"state\t%d", state]];
}

static NSString *Clean(NSString *text) {
    if (text == nil) return @"";
    text = [text stringByReplacingOccurrencesOfString:@"\t" withString:@" "];
    return [text stringByReplacingOccurrencesOfString:@"\n" withString:@" "];
}

- (void)start {
    self.wanted = YES;
    if (self.central == nil) self.central = [[CBCentralManager alloc] initWithDelegate:self queue:self.queue];
    else [self scan];
}

- (void)stop {
    self.wanted = NO;
    if (self.central.state == CBManagerStatePoweredOn) [self.central stopScan];
    if (self.active) [self.central cancelPeripheralConnection:self.active];
}

- (void)scan {
    if (!self.wanted || self.central.state != CBManagerStatePoweredOn) return;
    // A console this Mac is already connected to does not advertise.
    for (CBPeripheral *p in [self.central retrieveConnectedPeripheralsWithServices:@[Uuid(@"538AB800")]])
        [self found:p name:p.name rssi:@0];
    [self.central scanForPeripheralsWithServices:@[Uuid(@"538AB800")] options:@{CBCentralManagerScanOptionAllowDuplicatesKey: @YES}];
    if (self.active == nil) [self setStateAndTell:4];
}

- (void)found:(CBPeripheral *)p name:(NSString *)name rssi:(NSNumber *)rssi {
    NSString *identifier = p.identifier.UUIDString;
    self.seen[identifier] = p;
    // Advertisements repeat several times a second: once a second is plenty.
    NSDate *last = self.told[identifier];
    if (last && [last timeIntervalSinceNow] > -1.0) return;
    self.told[identifier] = [NSDate date];
    NSString *shown = name.length ? name : (p.name.length ? p.name : @"Puttr");
    [self push:[NSString stringWithFormat:@"found\t%@\t%@\t%d", identifier, Clean(shown), rssi.intValue]];
}

- (void)connect:(NSString *)identifier {
    dispatch_async(self.queue, ^{
        if (self.central.state != CBManagerStatePoweredOn) return;
        CBPeripheral *p = self.seen[identifier];
        if (p == nil) {
            NSUUID *uuid = [[NSUUID alloc] initWithUUIDString:identifier];
            if (uuid) p = [self.central retrievePeripheralsWithIdentifiers:@[uuid]].firstObject;
        }
        if (p == nil) { [self push:@"error\tThat console is not in range"]; return; }
        if (self.active && self.active != p) [self.central cancelPeripheralConnection:self.active];
        self.active = p;
        p.delegate = self;
        [self setStateAndTell:5];
        [self.central connectPeripheral:p options:nil];
    });
}

- (void)disconnect {
    dispatch_async(self.queue, ^{
        if (self.active) [self.central cancelPeripheralConnection:self.active];
    });
}

// ── The central ─────────────────────────────────────────────────────────────

- (void)centralManagerDidUpdateState:(CBCentralManager *)central {
    switch (central.state) {
        case CBManagerStatePoweredOn: [self scan]; break;
        case CBManagerStateUnauthorized: [self setStateAndTell:2]; break;
        case CBManagerStatePoweredOff: self.active = nil; [self setStateAndTell:3]; break;
        case CBManagerStateUnsupported: [self setStateAndTell:1]; break;
        default: break;
    }
}

- (void)centralManager:(CBCentralManager *)central didDiscoverPeripheral:(CBPeripheral *)peripheral advertisementData:(NSDictionary<NSString *, id> *)advertisementData RSSI:(NSNumber *)RSSI {
    [self found:peripheral name:advertisementData[CBAdvertisementDataLocalNameKey] rssi:RSSI];
}

- (void)centralManager:(CBCentralManager *)central didConnectPeripheral:(CBPeripheral *)peripheral {
    [peripheral discoverServices:@[Uuid(@"538AB800")]];
}

- (void)centralManager:(CBCentralManager *)central didFailToConnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    [self push:[NSString stringWithFormat:@"error\t%@", Clean(error.localizedDescription ?: @"Could not connect")]];
    [self lost:peripheral];
}

- (void)centralManager:(CBCentralManager *)central didDisconnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    [self lost:peripheral];
}

- (void)lost:(CBPeripheral *)peripheral {
    [self push:[NSString stringWithFormat:@"disconnected\t%@", peripheral.identifier.UUIDString]];
    if (self.active == peripheral) self.active = nil;
    if (self.central.state == CBManagerStatePoweredOn) [self setStateAndTell:self.wanted ? 4 : 3];
}

// ── The console ─────────────────────────────────────────────────────────────

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverServices:(NSError *)error {
    for (CBService *service in peripheral.services)
        [peripheral discoverCharacteristics:@[Uuid(@"538AB801"), Uuid(@"538AB802"), Uuid(@"538AB803"), Uuid(@"538AB804")] forService:service];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverCharacteristicsForService:(CBService *)service error:(NSError *)error {
    BOOL ball = NO;
    for (CBCharacteristic *c in service.characteristics) {
        if ([c.UUID isEqual:Uuid(@"538AB801")]) { [peripheral setNotifyValue:YES forCharacteristic:c]; ball = YES; }
        else if ([c.UUID isEqual:Uuid(@"538AB802")] || [c.UUID isEqual:Uuid(@"538AB804")]) [peripheral readValueForCharacteristic:c];
    }
    if (!ball) { [self push:@"error\tThis device has no ball path: is it a Puttr?"]; return; }
    [self push:[NSString stringWithFormat:@"connected\t%@\t%@", peripheral.identifier.UUIDString, Clean(peripheral.name.length ? peripheral.name : @"Puttr")]];
    [self setStateAndTell:6];
}

- (void)peripheral:(CBPeripheral *)peripheral didUpdateValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    if (error || characteristic.value == nil) return;
    NSString *text = [[NSString alloc] initWithData:characteristic.value encoding:NSUTF8StringEncoding];
    if (text == nil) return;
    NSString *kind = [characteristic.UUID isEqual:Uuid(@"538AB801")] ? @"ball" : [characteristic.UUID isEqual:Uuid(@"538AB802")] ? @"hole" : @"version";
    [self push:[NSString stringWithFormat:@"%@\t%@", kind, Clean(text)]];
}

// Writes text to one of the console's characteristics (Wi-Fi setup: 538AB803, "SSID;;PASSWORD").
- (void)write:(NSString *)head text:(NSString *)text {
    dispatch_async(self.queue, ^{
        CBPeripheral *p = self.active;
        CBCharacteristic *target = nil;
        for (CBService *s in p.services) for (CBCharacteristic *c in s.characteristics) if ([c.UUID isEqual:Uuid(head)]) target = c;
        if (target == nil) { [self push:[NSString stringWithFormat:@"wrote\t%@\tfail", head]]; return; }
        BOOL acked = (target.properties & CBCharacteristicPropertyWrite) != 0;
        [p writeValue:[text dataUsingEncoding:NSUTF8StringEncoding] forCharacteristic:target type:acked ? CBCharacteristicWriteWithResponse : CBCharacteristicWriteWithoutResponse];
        if (!acked) [self push:[NSString stringWithFormat:@"wrote\t%@\tok", head]];
    });
}

- (void)peripheral:(CBPeripheral *)peripheral didWriteValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    NSString *head = [characteristic.UUID.UUIDString substringToIndex:8];
    [self push:[NSString stringWithFormat:@"wrote\t%@\t%@", head.uppercaseString, error ? @"fail" : @"ok"]];
}

@end

// ── For Unity ───────────────────────────────────────────────────────────────

static PuttrBle *Shared(void) {
    static PuttrBle *shared;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ shared = [[PuttrBle alloc] init]; });
    return shared;
}

void PuttrBle_Start(void) { PuttrBle *b = Shared(); dispatch_async(b.queue, ^{ [b start]; }); }
void PuttrBle_Stop(void) { PuttrBle *b = Shared(); dispatch_async(b.queue, ^{ [b stop]; }); }
void PuttrBle_Connect(const char *identifier) { if (identifier) [Shared() connect:[NSString stringWithUTF8String:identifier]]; }
void PuttrBle_Disconnect(void) { [Shared() disconnect]; }
void PuttrBle_Write(const char *head, const char *text) { if (head && text) [Shared() write:[NSString stringWithUTF8String:head] text:[NSString stringWithUTF8String:text]]; }
int PuttrBle_State(void) { return Shared().state; }

// The next thing that happened, as UTF-8 text. Returns its length, or 0 when there is nothing waiting.
int PuttrBle_Poll(char *buffer, int capacity) {
    PuttrBle *b = Shared();
    NSString *line = nil;
    @synchronized (b.events) {
        if (b.events.count == 0) return 0;
        line = b.events.firstObject;
        [b.events removeObjectAtIndex:0];
    }
    const char *utf8 = line.UTF8String;
    int length = (int)strlen(utf8);
    if (length > capacity - 1) length = capacity - 1;
    memcpy(buffer, utf8, length);
    buffer[length] = 0;
    return length;
}
