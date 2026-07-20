// App Tracking Transparency bridge. Unity calls PW_RequestTracking() once at
// launch; on iOS 14+ this shows the system "Allow tracking?" dialog. The user's
// choice is reported back to the C# callback so AdMob can be initialized only
// after the prompt resolves (personalized-ads eligibility depends on it).
//
// On iOS < 14 there is no ATT framework: we report "authorized" immediately so
// the launch flow proceeds unchanged.
#import <Foundation/Foundation.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>
#import <AdSupport/AdSupport.h>

typedef void (*PW_TrackingCallback)(int status);

extern "C" void PW_RequestTracking(PW_TrackingCallback cb)
{
    if (@available(iOS 14, *)) {
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
            // status: 0 notDetermined, 1 restricted, 2 denied, 3 authorized
            if (cb) cb((int)status);
        }];
    } else {
        // Pre-iOS 14: tracking is allowed without a prompt.
        if (cb) cb(3);
    }
}
