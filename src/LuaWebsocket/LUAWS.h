#pragma once

#ifdef _WIN32
	#ifdef LUAWS_EXPORTS
		#define LUAWS_API __declspec(dllexport)
	#else
		#define LUAWS_API __declspec(dllimport)
	#endif
#else
    #define LUAWS_API
#endif