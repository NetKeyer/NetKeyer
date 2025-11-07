// ****************************************************************************
///*!	\file PushNotificationData.cs
// *	\brief Represents data from JSON push notification file
// *
// *	\copyright	Copyright 2025 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// */
// ****************************************************************************

using System;
using System.Collections.Generic;

namespace Flex.Smoothlake.FlexLib;

public record PushNotificationData(
    Guid Id,
    string Title,
    string BannerImageUrl,
    double? BannerImageHeight,
    double? BannerImageWidth,
    List<string> ContentBlocks,
    List<string> ModelTarget,
    List<string> ClientTarget,
    DateTime Expires,
    bool OnlyForAlpha
);