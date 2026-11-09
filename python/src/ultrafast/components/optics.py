"""Mirrors, lenses, gratings, prisms, plates, apertures, masks and other single optical elements.

Each CapWords name is an alias of a snake_case builder in :mod:`ultrafast.build` (the same function: signature, typed
return value and docstring are unchanged, so autocompletion is the same).
"""
from ..build import (mirror, lens, grating, grating_pair, prism, plate, aperture, soft_aperture, mask, wavefront, chirped_mirror, dispersion, substrate, interface, nd_filter, filter_element, coating_ripple, probe, user)

Mirror = mirror
Lens = lens
Grating = grating
GratingPair = grating_pair
Prism = prism
Plate = plate
Aperture = aperture
SoftAperture = soft_aperture
Mask = mask
Wavefront = wavefront
ChirpedMirror = chirped_mirror
Dispersion = dispersion
Substrate = substrate
Interface = interface
NdFilter = nd_filter
Filter = filter_element
CoatingRipple = coating_ripple
Probe = probe
UserElement = user

__all__ = ["Mirror", "Lens", "Grating", "GratingPair", "Prism", "Plate", "Aperture", "SoftAperture", "Mask", "Wavefront", "ChirpedMirror", "Dispersion", "Substrate", "Interface", "NdFilter", "Filter", "CoatingRipple", "Probe", "UserElement", "mirror", "lens", "grating", "grating_pair", "prism", "plate", "aperture", "soft_aperture", "mask", "wavefront", "chirped_mirror", "dispersion", "substrate", "interface", "nd_filter", "filter_element", "coating_ripple", "probe", "user"]
