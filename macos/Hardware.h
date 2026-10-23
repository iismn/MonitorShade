#import <Foundation/Foundation.h>
#import <CoreGraphics/CoreGraphics.h>
NS_ASSUME_NONNULL_BEGIN
NSDictionary *DMDisplayMetadata(CGDirectDisplayID display);
BOOL DMSetRotation(CGDirectDisplayID display, int degrees);
BOOL DMRotationSupported(CGDirectDisplayID display);
NSDictionary *DMReadBrightness(CGDirectDisplayID display);
NSDictionary *DMWriteBrightness(CGDirectDisplayID display, int value);
BOOL DMHardwareSelfTest(void);
NS_ASSUME_NONNULL_END
