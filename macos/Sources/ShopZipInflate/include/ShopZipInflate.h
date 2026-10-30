#ifndef SHOP_ZIP_INFLATE_H
#define SHOP_ZIP_INFLATE_H
#include <stddef.h>
#include <stdint.h>
int shop_inflate_raw(const uint8_t *input, size_t input_size, uint8_t *output, size_t output_size);
#endif
