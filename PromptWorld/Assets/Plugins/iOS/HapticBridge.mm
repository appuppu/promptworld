// Haptic feedback bridge. Unity calls PW_Haptic(style) to fire a short taptic
// pulse on iOS 10+ (light/medium/heavy impact, or a success notification). Used
// for button taps and the world-first-clear celebration so the UI feels alive.
// No-ops silently on unsupported devices.
#import <UIKit/UIKit.h>

extern "C" void PW_Haptic(int style)
{
    if (@available(iOS 10, *)) {
        if (style == 3) {
            UINotificationFeedbackGenerator *g = [[UINotificationFeedbackGenerator alloc] init];
            [g prepare];
            [g notificationOccurred:UINotificationFeedbackTypeSuccess];
        } else {
            UIImpactFeedbackStyle s = UIImpactFeedbackStyleLight;
            if (style == 1) s = UIImpactFeedbackStyleMedium;
            else if (style == 2) s = UIImpactFeedbackStyleHeavy;
            UIImpactFeedbackGenerator *g = [[UIImpactFeedbackGenerator alloc] initWithStyle:s];
            [g prepare];
            [g impactOccurred];
        }
    }
}
