#import "Hardware.h"
#import <IOKit/IOKitLib.h>
#import <dlfcn.h>
#import <objc/message.h>
#import <math.h>

// The IOKit transport mapping follows waydabber/m1ddc (MIT).
// MonitorPanel orientation is the same macOS API used by displayplacer (MIT).
typedef CFTypeRef AVService;
typedef AVService (*AVCreate)(CFAllocatorRef, io_service_t);
typedef IOReturn (*AVI2C)(AVService, uint32_t, uint32_t, void *, uint32_t);
static AVCreate avCreate;
static AVI2C avRead, avWrite;
static CFDictionaryRef (*displayInfo)(CGDirectDisplayID);
static int (*getNativeBrightness)(CGDirectDisplayID, float *);
static int (*setNativeBrightness)(CGDirectDisplayID, float);
static void loadAPIs(void) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        void *core = dlopen("/System/Library/Frameworks/CoreDisplay.framework/CoreDisplay", RTLD_LAZY);
        void *io = dlopen("/System/Library/Frameworks/IOKit.framework/IOKit", RTLD_LAZY);
        void *services = dlopen("/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices", RTLD_LAZY);
        dlopen("/System/Library/PrivateFrameworks/MonitorPanel.framework/MonitorPanel", RTLD_LAZY);
        displayInfo = core ? dlsym(core, "CoreDisplay_DisplayCreateInfoDictionary") : NULL;
        avCreate = io ? dlsym(io, "IOAVServiceCreateWithService") : NULL;
        avRead = io ? dlsym(io, "IOAVServiceReadI2C") : NULL;
        avWrite = io ? dlsym(io, "IOAVServiceWriteI2C") : NULL;
        getNativeBrightness = services ? dlsym(services, "DisplayServicesGetBrightness") : NULL;
        setNativeBrightness = services ? dlsym(services, "DisplayServicesSetBrightness") : NULL;
    });
}
static NSDictionary *infoFor(CGDirectDisplayID display) {
    loadAPIs();
    return displayInfo ? CFBridgingRelease(displayInfo(display)) : @{};
}
static io_registry_entry_t adapterFor(CGDirectDisplayID display) {
    NSString *path = infoFor(display)[@"IODisplayLocation"];
    return path ? IORegistryEntryCopyFromPath(kIOMainPortDefault, (__bridge CFStringRef)path) : 0;
}
static id property(io_registry_entry_t entry, NSString *key) {
    return CFBridgingRelease(IORegistryEntrySearchCFProperty(entry, kIOServicePlane,
        (__bridge CFStringRef)key, kCFAllocatorDefault, kIORegistryIterateRecursively));
}
NSDictionary *DMDisplayMetadata(CGDirectDisplayID display) {
    NSDictionary *info = infoFor(display);
    io_registry_entry_t adapter = adapterFor(display);
    NSMutableDictionary *result = [NSMutableDictionary dictionary];
    if (info[@"IODisplayLocation"]) result[@"location"] = info[@"IODisplayLocation"];
    if (info[@"kCGDisplayUUID"]) result[@"systemUUID"] = info[@"kCGDisplayUUID"];
    if (adapter) {
        NSDictionary *attrs = property(adapter, @"DisplayAttributes");
        NSDictionary *product = attrs[@"ProductAttributes"];
        if (product[@"AlphanumericSerialNumber"]) result[@"alphaSerial"] = product[@"AlphanumericSerialNumber"];
        if (product[@"ProductName"]) result[@"productName"] = product[@"ProductName"];
        id edid = property(adapter, @"EDID UUID");
        if ([edid isKindOfClass:NSString.class]) result[@"edidUUID"] = edid;
        IOObjectRelease(adapter);
    }
    return result;
}
static id panel(CGDirectDisplayID display) {
    loadAPIs();
    Class cls = NSClassFromString(@"MPDisplay");
    SEL sel = NSSelectorFromString(@"initWithCGSDisplayID:");
    if (!cls || ![cls instancesRespondToSelector:sel]) return nil;
    return ((id (*)(id, SEL, int))objc_msgSend)([cls alloc], sel, (int)display);
}
BOOL DMRotationSupported(CGDirectDisplayID display) {
    @try {
        id p = panel(display);
        SEL sel = NSSelectorFromString(@"canChangeOrientation");
        return p && [p respondsToSelector:sel] && ((BOOL (*)(id, SEL))objc_msgSend)(p, sel);
    } @catch (NSException *e) { return NO; }
}
BOOL DMSetRotation(CGDirectDisplayID display, int degrees) {
    if (degrees != 0 && degrees != 90 && degrees != 180 && degrees != 270) return NO;
    @try {
        id p = panel(display);
        SEL sel = NSSelectorFromString(@"setOrientation:");
        if (!p || ![p respondsToSelector:sel]) return NO;
        ((void (*)(id, SEL, int))objc_msgSend)(p, sel, degrees);
        return YES;
    } @catch (NSException *e) { return NO; }
}
static AVService transport(CGDirectDisplayID display, uint32_t *chip) {
    loadAPIs();
    if (!avCreate || !avRead || !avWrite || CGDisplayIsBuiltin(display)) return NULL;
    io_registry_entry_t adapter = adapterFor(display);
    if (!adapter) return NULL;
    uint64_t target = 0;
    IORegistryEntryGetRegistryEntryID(adapter, &target);
    IOObjectRelease(adapter);
    io_registry_entry_t root = IORegistryGetRootEntry(kIOMainPortDefault);
    io_iterator_t iter = 0;
    kern_return_t err = IORegistryEntryCreateIterator(root, kIOServicePlane, kIORegistryIterateRecursively, &iter);
    IOObjectRelease(root);
    if (err != KERN_SUCCESS) return NULL;
    BOOL matches = NO;
    AVService found = NULL;
    io_service_t entry;
    while ((entry = IOIteratorNext(iter))) {
        if (IOObjectConformsTo(entry, "IOMobileFramebuffer")) {
            uint64_t value = 0;
            IORegistryEntryGetRegistryEntryID(entry, &value);
            matches = target == value;
        } else if (matches && IOObjectConformsTo(entry, "DCPAVServiceProxy") &&
                   [property(entry, @"Location") isEqual:@"External"]) {
            found = avCreate(kCFAllocatorDefault, entry);
            io_registry_entry_t parent = 0;
            *chip = 0x37;
            if (IORegistryEntryGetParentEntry(entry, kIOServicePlane, &parent) == KERN_SUCCESS) {
                if ([property(parent, @"EPICProviderClass") isEqual:@"AppleDCPMCDP29XX"]) *chip = 0xB7;
                IOObjectRelease(parent);
            }
        }
        IOObjectRelease(entry);
        if (found) break;
    }
    IOObjectRelease(iter);
    return found;
}
static void readPacket(uint8_t *bytes) {
    bytes[0] = 0x82; bytes[1] = 0x01; bytes[2] = 0x10;
    bytes[3] = 0x6E ^ 0x51 ^ bytes[0] ^ bytes[1] ^ bytes[2];
}
static BOOL parseReply(uint8_t *b, int *current, int *maximum) {
    if (b[0] != 0x6E || (b[1] & 0x7F) != 8 || b[2] != 0x02 || b[3] != 0 || b[4] != 0x10) return NO;
    uint8_t checksum = 0x50;
    for (int i = 0; i < 11; i++) checksum ^= b[i];
    *maximum = ((int)b[6] << 8) | b[7];
    *current = ((int)b[8] << 8) | b[9];
    return checksum == 0 && *maximum > 0 && *current <= *maximum;
}
static NSDictionary *failure(NSString *reason) { return @{@"ok":@NO, @"error":reason}; }
NSDictionary *DMReadBrightness(CGDirectDisplayID display) {
    if (CGDisplayIsBuiltin(display)) {
        loadAPIs();
        float value = 0;
        if (!getNativeBrightness || !setNativeBrightness || getNativeBrightness(display, &value) != 0 ||
            !isfinite(value) || value < 0 || value > 1) {
            return failure(@"맥북 내장 화면의 밝기를 읽지 못했습니다. 화면이 활성화되어 있는지 확인하세요.");
        }
        return @{@"ok":@YES, @"current":@(lroundf(value * 10000)), @"maximum":@10000};
    }
    uint32_t chip = 0x37;
    AVService service = transport(display, &chip);
    if (!service) return failure(@"이 연결에서 DDC/CI 통신 경로를 열 수 없습니다.");
    int current = 0, maximum = 0;
    BOOL valid = NO;
    for (int attempt = 0; attempt < 3 && !valid; attempt++) {
        uint8_t request[4] = {}, response[12] = {};
        readPacket(request);
        usleep(50000);
        if (avWrite(service, chip, 0x51, request, 4) != KERN_SUCCESS) continue;
        usleep(70000);
        if (avRead(service, chip, 0x51, response, 12) == KERN_SUCCESS) valid = parseReply(response, &current, &maximum);
    }
    CFRelease(service);
    return valid ? @{@"ok":@YES, @"current":@(current), @"maximum":@(maximum)} :
        failure(@"DDC/CI 밝기 응답이 없습니다. 모니터 OSD의 DDC/CI 설정과 HDR 모드를 확인하세요.");
}
NSDictionary *DMWriteBrightness(CGDirectDisplayID display, int value) {
    if (value < 0 || value > 65535) return failure(@"밝기 값이 범위를 벗어났습니다.");
    if (CGDisplayIsBuiltin(display)) {
        if (value > 10000) return failure(@"내장 화면 밝기 값이 범위를 벗어났습니다.");
        loadAPIs();
        return setNativeBrightness && setNativeBrightness(display, (float)value / 10000) == 0 ?
            @{@"ok":@YES} : failure(@"맥북 내장 화면의 밝기를 적용하지 못했습니다.");
    }
    uint32_t chip = 0x37;
    AVService service = transport(display, &chip);
    if (!service) return failure(@"DDC/CI 연결이 끊겼습니다.");
    uint8_t bytes[6] = {0x84, 0x03, 0x10, (value >> 8) & 255, value & 255, 0};
    bytes[5] = 0x6E ^ 0x51 ^ bytes[0] ^ bytes[1] ^ bytes[2] ^ bytes[3] ^ bytes[4];
    IOReturn result = kIOReturnError;
    for (int i = 0; i < 2; i++) {
        usleep(50000);
        result = avWrite(service, chip, 0x51, bytes, 6);
        if (result != KERN_SUCCESS) break;
    }
    CFRelease(service);
    return result == KERN_SUCCESS ? @{@"ok":@YES} : failure(@"모니터에 DDC/CI 밝기를 보내지 못했습니다.");
}
BOOL DMHardwareSelfTest(void) {
    uint8_t request[4]; readPacket(request);
    NSCAssert(request[3] == 0xAC, @"Get VCP checksum");
    uint8_t response[12] = {0x6E,0x88,0x02,0,0x10,0,0x01,0x90,0,0xC8,0,0};
    response[10] = 0x50;
    for (int i = 0; i < 10; i++) response[10] ^= response[i];
    int c, m;
    NSCAssert(parseReply(response,&c,&m) && c == 200 && m == 400, @"16-bit VCP decode");
    response[10] ^= 1;
    NSCAssert(!parseReply(response,&c,&m), @"Reject invalid checksum");
    response[3] = 1;
    NSCAssert(!parseReply(response,&c,&m), @"Reject unsupported VCP");
    return YES;
}
