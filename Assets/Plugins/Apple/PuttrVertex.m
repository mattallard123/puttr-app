// The Vertex putter sensor, for Unity on iOS (puttr-app components/vertex/VertexBle.js).
//
// Service 0000fff0-0904-4eb4-8c23-a5f94f5034ee: fff1 control and status (10 bytes), fff2 putt data
// (six 20-byte packets a putt), fff3 raw data (subscribed, unused). Sensors advertise as "vtx..." or
// "vertex...". Once connected and subscribed the app's C# (Vertex.cs) sends the live-mode command.
//
// Unity polls; each line is one of:
//
//   vstate \t n                  1 unsupported, 2 not allowed, 3 off, 4 on
//   vfound \t id \t name \t rssi \t manufacturer-hex
//   vconnected \t id \t name \t 1 if fff1 takes acknowledged writes
//   vdisconnected \t id
//   vstatus \t hex               fff1, read or notified
//   vdata \t hex                 fff2
//   verror \t text
#import <Foundation/Foundation.h>
#import <CoreBluetooth/CoreBluetooth.h>

static CBUUID *VxUuid(NSString *head) { return [CBUUID UUIDWithString:[head stringByAppendingString:@"-0904-4eb4-8c23-a5f94f5034ee"]]; }

static NSString *VxHex(NSData *data) {
    NSMutableString *s = [NSMutableString stringWithCapacity:data.length * 2];
    const unsigned char *b = data.bytes;
    for (NSUInteger i = 0; i < data.length; i++) [s appendFormat:@"%02x", b[i]];
    return s;
}

static NSString *VxClean(NSString *t) { return t ? [[t stringByReplacingOccurrencesOfString:@"\t" withString:@" "] stringByReplacingOccurrencesOfString:@"\n" withString:@" "] : @""; }

@interface PuttrVertex : NSObject <CBCentralManagerDelegate, CBPeripheralDelegate>
@property (strong) CBCentralManager *central;
@property (strong) dispatch_queue_t queue;
@property (strong) NSMutableDictionary<NSString *, CBPeripheral *> *seen;
@property (strong) NSMutableArray<NSString *> *events;
@property (strong) CBPeripheral *active;
@property (strong) CBCharacteristic *control;
@property (assign) BOOL scanning;
@end

@implementation PuttrVertex

- (instancetype)init {
    if ((self = [super init])) {
        _seen = [NSMutableDictionary dictionary];
        _events = [NSMutableArray array];
        _queue = dispatch_queue_create("co.puttr.vertex", DISPATCH_QUEUE_SERIAL);
    }
    return self;
}

- (void)push:(NSString *)line {
    @synchronized (self.events) {
        if (self.events.count > 2000) [self.events removeObjectAtIndex:0];
        [self.events addObject:line];
    }
}

- (void)scan:(BOOL)on {
    dispatch_async(self.queue, ^{
        self.scanning = on;
        if (self.central == nil) { self.central = [[CBCentralManager alloc] initWithDelegate:self queue:self.queue]; return; }
        if (self.central.state != CBManagerStatePoweredOn) return;
        // Sensors do not advertise the service, so the scan is unfiltered and the name decides.
        if (on) [self.central scanForPeripheralsWithServices:nil options:@{CBCentralManagerScanOptionAllowDuplicatesKey: @NO}];
        else [self.central stopScan];
    });
}

- (void)connect:(NSString *)identifier {
    dispatch_async(self.queue, ^{
        CBPeripheral *p = self.seen[identifier];
        if (p == nil) {
            NSUUID *uuid = [[NSUUID alloc] initWithUUIDString:identifier];
            if (uuid) p = [self.central retrievePeripheralsWithIdentifiers:@[uuid]].firstObject;
        }
        if (p == nil) { [self push:@"verror\tThat sensor is not in range"]; return; }
        [self.central stopScan];
        if (self.active && self.active != p) [self.central cancelPeripheralConnection:self.active];
        self.active = p;
        p.delegate = self;
        [self.central connectPeripheral:p options:nil];
    });
}

- (void)disconnect {
    dispatch_async(self.queue, ^{ if (self.active) [self.central cancelPeripheralConnection:self.active]; });
}

- (void)write:(NSData *)data {
    dispatch_async(self.queue, ^{
        if (self.active == nil || self.control == nil) { [self push:@"verror\tNot connected"]; return; }
        BOOL acked = (self.control.properties & CBCharacteristicPropertyWrite) != 0;
        [self.active writeValue:data forCharacteristic:self.control type:acked ? CBCharacteristicWriteWithResponse : CBCharacteristicWriteWithoutResponse];
    });
}

- (void)readStatus {
    dispatch_async(self.queue, ^{ if (self.active && self.control) [self.active readValueForCharacteristic:self.control]; });
}

- (void)centralManagerDidUpdateState:(CBCentralManager *)central {
    int s = central.state == CBManagerStatePoweredOn ? 4 : central.state == CBManagerStateUnauthorized ? 2 : central.state == CBManagerStatePoweredOff ? 3 : central.state == CBManagerStateUnsupported ? 1 : 0;
    if (s) [self push:[NSString stringWithFormat:@"vstate\t%d", s]];
    if (s == 4 && self.scanning) [self.central scanForPeripheralsWithServices:nil options:nil];
}

- (void)centralManager:(CBCentralManager *)central didDiscoverPeripheral:(CBPeripheral *)peripheral advertisementData:(NSDictionary<NSString *, id> *)ad RSSI:(NSNumber *)RSSI {
    NSString *name = ad[CBAdvertisementDataLocalNameKey] ?: peripheral.name;
    NSString *lower = name.lowercaseString;
    if (!([lower hasPrefix:@"vtx"] || [lower hasPrefix:@"vertex"])) return;
    NSString *identifier = peripheral.identifier.UUIDString;
    self.seen[identifier] = peripheral;
    NSData *maker = ad[CBAdvertisementDataManufacturerDataKey];
    [self push:[NSString stringWithFormat:@"vfound\t%@\t%@\t%d\t%@", identifier, VxClean(name), RSSI.intValue, maker ? VxHex(maker) : @""]];
}

- (void)centralManager:(CBCentralManager *)central didConnectPeripheral:(CBPeripheral *)peripheral {
    [peripheral discoverServices:@[VxUuid(@"0000fff0")]];
}

- (void)centralManager:(CBCentralManager *)central didFailToConnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    [self push:[NSString stringWithFormat:@"verror\t%@", VxClean(error.localizedDescription ?: @"Could not connect")]];
    [self push:[NSString stringWithFormat:@"vdisconnected\t%@", peripheral.identifier.UUIDString]];
}

- (void)centralManager:(CBCentralManager *)central didDisconnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    if (self.active == peripheral) { self.active = nil; self.control = nil; }
    [self push:[NSString stringWithFormat:@"vdisconnected\t%@", peripheral.identifier.UUIDString]];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverServices:(NSError *)error {
    BOOL any = NO;
    for (CBService *s in peripheral.services) if ([s.UUID isEqual:VxUuid(@"0000fff0")]) {
        any = YES;
        [peripheral discoverCharacteristics:@[VxUuid(@"0000fff1"), VxUuid(@"0000fff2"), VxUuid(@"0000fff3")] forService:s];
    }
    if (!any) [self push:@"verror\tThis is not a Vertex sensor"];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverCharacteristicsForService:(CBService *)service error:(NSError *)error {
    BOOL data = NO;
    for (CBCharacteristic *c in service.characteristics) {
        if ([c.UUID isEqual:VxUuid(@"0000fff1")]) { self.control = c; [peripheral readValueForCharacteristic:c]; [peripheral setNotifyValue:YES forCharacteristic:c]; }
        else if ([c.UUID isEqual:VxUuid(@"0000fff2")]) { data = YES; [peripheral setNotifyValue:YES forCharacteristic:c]; }
        else if ([c.UUID isEqual:VxUuid(@"0000fff3")]) [peripheral setNotifyValue:YES forCharacteristic:c];
    }
    if (!data || self.control == nil) { [self push:@"verror\tThe sensor's putt data was not found"]; return; }
    BOOL acked = (self.control.properties & CBCharacteristicPropertyWrite) != 0;
    [self push:[NSString stringWithFormat:@"vconnected\t%@\t%@\t%d", peripheral.identifier.UUIDString, VxClean(peripheral.name ?: @"Vertex"), acked ? 1 : 0]];
}

- (void)peripheral:(CBPeripheral *)peripheral didUpdateValueForCharacteristic:(CBCharacteristic *)c error:(NSError *)error {
    if (error || c.value == nil) return;
    if ([c.UUID isEqual:VxUuid(@"0000fff1")]) [self push:[@"vstatus\t" stringByAppendingString:VxHex(c.value)]];
    else if ([c.UUID isEqual:VxUuid(@"0000fff2")]) [self push:[@"vdata\t" stringByAppendingString:VxHex(c.value)]];
}

@end

static PuttrVertex *VxShared(void) {
    static PuttrVertex *shared;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ shared = [[PuttrVertex alloc] init]; });
    return shared;
}

void PuttrVertex_Scan(int on) { [VxShared() scan:on != 0]; }
void PuttrVertex_Connect(const char *identifier) { if (identifier) [VxShared() connect:[NSString stringWithUTF8String:identifier]]; }
void PuttrVertex_Disconnect(void) { [VxShared() disconnect]; }
void PuttrVertex_ReadStatus(void) { [VxShared() readStatus]; }
void PuttrVertex_Write(const unsigned char *bytes, int length) { if (bytes && length > 0) [VxShared() write:[NSData dataWithBytes:bytes length:length]]; }

int PuttrVertex_Poll(char *buffer, int capacity) {
    PuttrVertex *v = VxShared();
    NSString *line = nil;
    @synchronized (v.events) {
        if (v.events.count == 0) return 0;
        line = v.events.firstObject;
        [v.events removeObjectAtIndex:0];
    }
    const char *utf8 = line.UTF8String;
    int length = (int)strlen(utf8);
    if (length > capacity - 1) length = capacity - 1;
    memcpy(buffer, utf8, length);
    buffer[length] = 0;
    return length;
}
