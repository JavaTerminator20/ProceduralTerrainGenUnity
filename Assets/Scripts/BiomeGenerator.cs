using System;
using System.Linq;
using Unity.Mathematics;
using Unity.VisualScripting;
using System.Collections.Generic;
using UnityEngine;

public class BiomeGenerator {
    // Determine biome from temperature and humidity noise values.
    // Noise values are expected in range [0,1].
    // Decision table used (temp rows × humidity columns):
    // Temp bins: cold (<=0.25), cool (<=0.4), temperate (<=0.6), warm/hot (>0.6)
    // Humidity bins: dry (<=0.3), semi (<=0.6), wet (>0.6)
    // Mapping (biome indices correspond to `biomes` array):
    // cold+dry  -> Tundra (0)
    // cold+wet  -> Taiga  (1)
    // cool+dry  -> Desert/Grassland (4 or 3)
    // cool+semi -> Grassland (3)
    // cool+wet  -> Temperate Forest (2)
    // temperate+dry  -> Grassland (3)
    // temperate+semi -> Temperate Forest (2)
    // temperate+wet  -> Rainforest (6)
    // warm/hot+dry  -> Desert (4)
    // warm/hot+semi -> Savanna (5)
    // warm/hot+wet  -> Rainforest (6)


    // MapGenerator variables regarding noise generation of biomes
    MapGenerator mapGen;
    public BiomeGenerator(MapGenerator mapGen) {
        this.mapGen = mapGen;
    }

    public static Biome[] biomes = {
        new Biome {
            name = "Tundra",
            color = new Color(0.6f, 0.8f, 0.8f),
            biomeMeshHeightMultiplier = 400f,
            // Curve: flat low, gentle rise after 0.5, sharp spike only near 1.0 (mostly flat frozen plains, occasional rocky peaks)
            biomeMeshHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            idealTemp     = 0.05f,
            idealHumidity = 0.20f,
            spread        = 0.02f,
            heightColors = new HeightColor[] {
                new HeightColor { height = 0.15f, color = new Color(0.1f,  0.2f,  0.3f)  }, // Dark Icy Water
                new HeightColor { height = 0.25f, color = new Color(0.4f,  0.5f,  0.6f)  }, // Frozen Shore
                new HeightColor { height = 0.50f, color = new Color(0.7f,  0.7f,  0.7f)  }, // Lichen/Permafrost
                new HeightColor { height = 0.85f, color = new Color(0.5f,  0.5f,  0.5f)  }, // Dark Rock
                new HeightColor { height = 1.00f, color = Color.white                     }  // Snow
            }
        },
        new Biome {
            name = "Taiga",
            color = new Color(0.1f, 0.4f, 0.2f),
            biomeMeshHeightMultiplier = 600f,
            // Curve: slow rise until 0.5, steeper from 0.6–0.85, sharp at top (hilly with real mountains)
            biomeMeshHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            idealTemp     = 0.20f,
            idealHumidity = 0.40f,
            spread        = 0.02f,
            heightColors = new HeightColor[] {
                new HeightColor { height = 0.15f, color = new Color(0.05f, 0.15f, 0.25f) }, // Deep Blue Water
                new HeightColor { height = 0.25f, color = new Color(0.35f, 0.3f,  0.2f)  }, // Cold Dirt
                new HeightColor { height = 0.60f, color = new Color(0.1f,  0.3f,  0.15f) }, // Pine Green
                new HeightColor { height = 0.85f, color = new Color(0.4f,  0.4f,  0.45f) }, // Blueish Rock
                new HeightColor { height = 1.00f, color = Color.white                     }  // Snow
            }
        },
        new Biome {
            name = "Temperate Forest",
            color = new Color(0.2f, 0.5f, 0.1f),
            biomeMeshHeightMultiplier = 550f,
            // Curve: flat until 0.3 (lowland forest), moderate rise 0.3–0.7, steep 0.7–1.0
            biomeMeshHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            idealTemp     = 0.55f,
            idealHumidity = 0.65f,
            spread        = 0.15f,
            heightColors = new HeightColor[] {
                new HeightColor { height = 0.15f, color = new Color(0.1f,  0.3f,  0.5f)  }, // Blue Water
                new HeightColor { height = 0.22f, color = new Color(0.76f, 0.7f,  0.5f)  }, // Sand/Beach
                new HeightColor { height = 0.65f, color = new Color(0.2f,  0.4f,  0.1f)  }, // Forest Green
                new HeightColor { height = 0.85f, color = new Color(0.5f,  0.5f,  0.5f)  }, // Grey Mountain
                new HeightColor { height = 1.00f, color = Color.white                     }  // Snow
            }
        },
        new Biome {
            name = "Grassland",
            color = new Color(0.4f, 0.7f, 0.2f),
            biomeMeshHeightMultiplier = 300f,
            // Curve: very flat until 0.6, short steep rise at the end (mostly plains, rare hills)
            biomeMeshHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            idealTemp     = 0.50f,
            idealHumidity = 0.35f,
            spread        = 0.02f,
            heightColors = new HeightColor[] {
                new HeightColor { height = 0.15f, color = new Color(0.1f,  0.4f,  0.6f)  }, // Water
                new HeightColor { height = 0.25f, color = new Color(0.4f,  0.5f,  0.2f)  }, // Lush Shore
                new HeightColor { height = 0.60f, color = new Color(0.5f,  0.8f,  0.3f)  }, // Bright Grass
                new HeightColor { height = 0.85f, color = new Color(0.6f,  0.55f, 0.5f)  }, // Light Rock
                new HeightColor { height = 1.00f, color = Color.white                     }  // Snow
            }
        },
        new Biome {
            name = "Desert",
            color = new Color(0.9f, 0.8f, 0.5f),
            biomeMeshHeightMultiplier = 350f,
            // Curve: rises steadily but never too steep — dunes and plateaus, no sharp peaks
            biomeMeshHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            idealTemp     = 0.85f,
            idealHumidity = 0.10f,
            spread        = 0.02f,
            heightColors = new HeightColor[] {
                new HeightColor { height = 0.15f, color = new Color(0.1f,  0.5f,  0.7f)  }, // Oasis Blue
                new HeightColor { height = 0.25f, color = new Color(0.95f, 0.9f,  0.6f)  }, // Pale Sand
                new HeightColor { height = 0.65f, color = new Color(0.85f, 0.6f,  0.3f)  }, // Orange Dunes
                new HeightColor { height = 0.85f, color = new Color(0.6f,  0.35f, 0.2f)  }, // Red Rock
                new HeightColor { height = 1.00f, color = Color.white                     }  // Snow (rare plateau tops)
            }
        },
        new Biome {
            name = "Savanna",
            color = new Color(0.7f, 0.6f, 0.2f),
            biomeMeshHeightMultiplier = 280f,
            // Curve: very flat overall, slight bump at end — savanna is almost entirely flat
            biomeMeshHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            idealTemp     = 0.75f,
            idealHumidity = 0.30f,
            spread        = 0.02f,
            heightColors = new HeightColor[] {
                new HeightColor { height = 0.15f, color = new Color(0.1f,  0.3f,  0.4f)  }, // Muddy Water
                new HeightColor { height = 0.25f, color = new Color(0.5f,  0.4f,  0.3f)  }, // Dry Dirt
                new HeightColor { height = 0.65f, color = new Color(0.7f,  0.6f,  0.3f)  }, // Sallow Grass
                new HeightColor { height = 0.85f, color = new Color(0.5f,  0.45f, 0.4f)  }, // Brown Rock
                new HeightColor { height = 1.00f, color = Color.white                     }  // Snow
            }
        },
        new Biome {
            name = "Rainforest",
            color = new Color(0.0f, 0.3f, 0.1f),
            biomeMeshHeightMultiplier = 500f,
            // Curve: flat lowlands (jungle floor), then rises sharply after 0.55 (jungle highlands are dramatic)
            biomeMeshHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            idealTemp     = 0.80f,
            idealHumidity = 0.90f,
            spread        = 0.02f,
            heightColors = new HeightColor[] {
                new HeightColor { height = 0.15f, color = new Color(0.0f,  0.2f,  0.3f)  }, // Dark Tropical Water
                new HeightColor { height = 0.25f, color = new Color(0.2f,  0.4f,  0.2f)  }, // Mossy Banks
                new HeightColor { height = 0.70f, color = new Color(0.05f, 0.35f, 0.1f)  }, // Deep Jungle Green
                new HeightColor { height = 0.90f, color = new Color(0.4f,  0.45f, 0.4f)  }, // Overgrown Rock
                new HeightColor { height = 1.00f, color = Color.white                     }  // Snow
            }
        },
        new Biome {
            name = "Ocean",
            color = new Color(0.0f, 0.2f, 0.6f),
            biomeMeshHeightMultiplier = 100f,
            biomeMeshHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            idealTemp     = 0.5f,
            idealHumidity = 0.5f,
            spread        = 0.5f,
            heightColors = new HeightColor[] {
                new HeightColor { height = 0.30f, color = new Color(0.0f,  0.1f,  0.4f)  }, // Deep Ocean
                new HeightColor { height = 0.60f, color = new Color(0.0f,  0.2f,  0.6f)  }, // Shallow Ocean
                new HeightColor { height = 0.80f, color = new Color(0.76f, 0.7f,  0.5f)  }, // Beach
                new HeightColor { height = 1.00f, color = new Color(0.6f,  0.6f,  0.6f)  }, // Coastal Rock
            }
        },
        new Biome {
            name = "Mountains",
            color = new Color(0.5f, 0.5f, 0.5f),
            biomeMeshHeightMultiplier = 900f,
            biomeMeshHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            idealTemp     = 0.5f,
            idealHumidity = 0.5f,
            spread        = 0.5f,
            heightColors = new HeightColor[] {
                new HeightColor { height = 0.30f, color = new Color(0.3f,  0.3f,  0.35f) }, // Low Rock
                new HeightColor { height = 0.60f, color = new Color(0.5f,  0.5f,  0.5f)  }, // Grey Rock
                new HeightColor { height = 0.80f, color = new Color(0.6f,  0.6f,  0.65f) }, // High Rock
                new HeightColor { height = 1.00f, color = Color.white                     }  // Snow Cap
            }
        }
        // Ocean = index 7, Mountains = index 8
    };

    // creates heightMap, colorMap, biomeMap and biomeWeightsMap for a given chunk 
    // mapChunkSize is 241 (239 + 2) (zaradi borderja)
    public BiomeMapData GenerateBiomeMapData(Vector2 center, int mapChunkSize) {

        float[,] heightMap = mapGen.GetChunkHeightMap(center, mapChunkSize);
        float[,] continentalnessMap = mapGen.GetChunkContinentalnessMap(center, mapChunkSize);

        float[,] tempNoiseMap = mapGen.GetChunkTemperatureMap(center, mapChunkSize);
        float[,] humidityNoiseMap = mapGen.GetChunkHumidityMap(center, mapChunkSize);

        Color[] colorMap = new Color[mapChunkSize * mapChunkSize];
        Biome[,] biomeMap = new Biome[mapChunkSize, mapChunkSize];
        float[,,] biomeWeightsMap = new float[mapChunkSize, mapChunkSize, biomes.Length]; // 3D array to store blend weights for each biome at each point

        // vegetation vars
        Dictionary<Vector2, Vegetation> vegetationPosition = new Dictionary<Vector2, Vegetation>();
        //int spacing = 20;
        //float density = 0.03f;
        System.Random rndm = new System.Random(1235123 * (int)center.x + 1239012 * (int)center.y);

        float oceanThreshold = mapGen.oceanThreshold;    // exposed in the inspector - the rest of the coastline math below is expressed relative to this, so it can be tuned freely

        // blending continent noise with height noise
        float[,] blendedHeightMap = new float[mapChunkSize, mapChunkSize];
        for (int y = 0; y < mapChunkSize; y++) {
            for (int x = 0; x < mapChunkSize; x++) {
                // blend local height with continentalness 
                blendedHeightMap[x, y] = 0.4f * heightMap[x, y] + 0.6f * continentalnessMap[x, y];
            }
        }


        for (int x = 0; x < mapChunkSize; x += 1) {
            for (int y = 0; y < mapChunkSize; y += 1) {

                float temp = tempNoiseMap[x, y];
                float humidity = humidityNoiseMap[x, y];
                float continentalness = continentalnessMap[x, y]; // -1 to 1


                // KINDA USELESS?? ------------------------------------------------------------------------------
                int biomeIndex = 0;

                // Continentalness overrides climate — check these first
                if (continentalness < 0.35f) {
                    biomeIndex = 7;                                     // Ocean — below sea level regardless of climate

                } else if (continentalness > 0.8f) {
                    //Debug.Log("High continentalness at (" + x + "," + y + "): " + continentalness);
                    biomeIndex = 8;                                     // Mountains — high enough that climate doesn't matter

                } else {
                    // Normal climate-based selection for mid-range continentalness
                    if (temp <= 0.25f) {
                        biomeIndex = (humidity > 0.5f) ? 1 : 0;         // Taiga : Tundra
                    } else if (temp <= 0.4f) {
                        if (humidity <= 0.3f) biomeIndex = 4;           // Desert
                        else if (humidity <= 0.6f) biomeIndex = 3;      // Grassland
                        else biomeIndex = 2;                            // Temperate Forest
                    } else if (temp <= 0.6f) {
                        if (humidity <= 0.3f) biomeIndex = 3;           // Grassland
                        else if (humidity <= 0.7f) biomeIndex = 2;      // Temperate Forest
                        else biomeIndex = 6;                            // Rainforest
                    } else {
                        if (humidity <= 0.3f) biomeIndex = 4;           // Desert
                        else if (humidity <= 0.6f) biomeIndex = 5;      // Savanna
                        else biomeIndex = 6;     // Rainforest
                    }
                }

                if (biomeIndex < 0) biomeIndex = 0;
                if (biomeIndex >= biomes.Length) biomeIndex = biomes.Length - 1;
                // --------------------------------------------------------------------------------------------------

                float sum = 0;

                // 1. Calculate Weights using the Plateau + Spread logic
                for (int i = 0; i < biomes.Length; i++) {
                    float weight = biomes[i].GetBlendFactor(temp, humidity, continentalnessMap[x, y]);
                    biomeWeightsMap[x, y, i] = weight;
                    sum += weight;
                }

                // 2. Normalize and Sharpen
                float sharpness = 1f;   // Adjust for tighter borders
                float sharpSum = 0;
                for (int i = 0; i < biomes.Length; i++) {
                    // Normalize first to 0-1 range
                    float normalized = sum > 0 ? biomeWeightsMap[x, y, i] / sum : 0f;
                    // Sharpen
                    biomeWeightsMap[x, y, i] = Mathf.Pow(normalized, sharpness);
                    sharpSum += biomeWeightsMap[x, y, i];
                }

                // 3. Final Re-normalization and Winner Selection: after sharpening, we need to re-normalize to ensure weights still sum to 1 (otherwise they don't)
                for (int i = 0; i < biomes.Length; i++) {
                    biomeWeightsMap[x, y, i] /= (sharpSum > 0) ? sharpSum : 1f;
                }

                float[] biomeColorWeights = new float[biomes.Length];       // se uporablja zato da ne rabimo spreminjati biomeColorWeights arraya ampak pri barvanju delamo samo s tem
                for (int i = 0; i < biomes.Length; i++) {
                    biomeColorWeights[i] = biomeWeightsMap[x, y, i];
                }

                int winningBiomeIndex = -1;

                //biomeMap[x, y] = biomes[biomeIndex];                    // we store the biome in the biomeMap

                //colorMap[y * mapChunkSize + x] = biomes[biomeIndex].color;    // this displays biome with a single color, ignoring height variation

                // -----------------TERRAIN COLOR LOGIC------------------------

                // CONSTANTS
                const float beachWidth = 0.034f;
                float beachThreshold = oceanThreshold - beachWidth;                     // threshold for continentalness above (and below oceanThreshold) which we consider the area to be beach (coastal strip)
                const float outterGradientDelta = 0.02f;                 // od oceanThresholda, koliko bo velik gradient navzven proti celini - kako dolgi je prehod kjer eliminiramo ocean biome v kontinent

                // these describe the coastal band in terms *relative* to beachThreshold/oceanThreshold (ratio 0 = ocean edge, 1 = land edge)
                // instead of absolute continentalness/height numbers, so the whole coast keeps looking the same no matter where oceanThreshold is set
                const float coastEdgeHeight = 0.51f;                     // blendedHeightMap value targeted right at the land-facing edge of the coastal band (ratio = 1)
                const float coastEdgeHeightRange = 0.07f;                // width (in blendedHeightMap units) of the oceanSupression falloff below coastEdgeHeight
                const float oceanEdgeSmoothingFraction = 0.3f;           // fraction of beachWidth (nearest the ocean edge) used to smooth into the true ocean height, avoiding a sharp ring
                const float sandEdgeStartRatio = 0.7f;                   // ratio across the coastal band at which we start blending toward pure sand color

                // BLENDING BIOME COLORS BASED ON HEIGHT AND WEIGHTS
                float tempHeight = blendedHeightMap[x, y];

                // inside an ocean biome (including the coastal strip)
                if (continentalnessMap[x, y] <= oceanThreshold) {
                    // ratio across the coastal band: 0 at the ocean-facing edge (beachThreshold), 1 at the land-facing edge (oceanThreshold)
                    float coastRatio = continentalnessMap[x, y] > beachThreshold
                        ? Mathf.InverseLerp(beachThreshold, oceanThreshold, continentalnessMap[x, y])
                        : 0f;

                    // beach color for the narrow coastal strip - we modify the height of the terrain to achieve coast like colors at the edge of water
                    if (continentalnessMap[x, y] > beachThreshold) {
                        blendedHeightMap[x, y] = Mathf.Lerp(0.7f * heightMap[x, y], coastEdgeHeight, coastRatio);   // making beach blend - height eases from the raw terrain height towards a fixed coast-edge height as we approach land

                        // this creates transition from the above blended coastal transition to the actual height of ocean (saved in tempHeight) - without this you get nasty sharp ring around the coast in the water
                        float oceanEdgeSmoothWidth = beachWidth * oceanEdgeSmoothingFraction;
                        if (continentalnessMap[x, y] < beachThreshold + oceanEdgeSmoothWidth) {
                            float ratio2 = Mathf.InverseLerp(beachThreshold + oceanEdgeSmoothWidth, beachThreshold, continentalnessMap[x, y]);
                            ratio2 = (float)Math.Pow(ratio2, 0.8f);         //naredimo funkcijo da je prehod pocasen na strani oceana
                            blendedHeightMap[x, y] = ratio2 * tempHeight + (1.0f - ratio2) * blendedHeightMap[x, y];
                        }
                    }

                    // assign the color corresponding to the height (previously manipulated)
                    Color biomeColor = EvaluateBiomeColorAtHeightWithTransition(biomes[7], blendedHeightMap[x, y], biomeWeightsMap[x, y, 7]);

                    // blend in a clean sand color right at the land-facing edge of the coastal band, so there's always a visible beach strip
                    // regardless of how the height/continentalness gradient happens to line up
                    if (coastRatio > sandEdgeStartRatio) {
                        float sandRatio = Mathf.InverseLerp(sandEdgeStartRatio, 1f, coastRatio);
                        biomeColor = Color.Lerp(biomeColor, biomes[7].heightColors[2].color, sandRatio);
                    }

                    colorMap[y * mapChunkSize + x] = biomeColor;

                    // MANIPULATION OF TERRAIN HEIGHT AT THE COAST - we create a gradual slope from the ocean to the land
                    // zato da je morje ravno takoj ko pridemo iz plaze - da ni vkrivljeno
                    float sum1 = 0;
                    float oceanSupression = Mathf.InverseLerp(coastEdgeHeight - coastEdgeHeightRange, coastEdgeHeight, blendedHeightMap[x, y]); // suppress all other biomes near the water part of coast, but keep full biome weights once we are far enough from the coast
                    oceanSupression = Mathf.Max(0.000001f, oceanSupression); // prevent division by zero
                    for (int k = 0; k < biomes.Length; k++) {                                        // we use blendedHeightMap to match the shape of the beach (that was created by manipulating the blendedHeightMap before)
                        if (k == 7) {                                                                // suppress non-ocean biomes in ocean areas, but with a smooth transition near the coast
                            biomeWeightsMap[x, y, k] *= Mathf.Pow(oceanSupression, -3f);              // Pow is used to create a more gradual transition from water to beach and then it quickly rises 
                        }
                        sum1 += biomeWeightsMap[x, y, k];
                    }

                    // we have to normalize again after suppression - so the weights sum up to 1
                    for (int k = 0; k < biomes.Length; k++) {
                        biomeWeightsMap[x, y, k] /= sum1 > 0 ? sum1 : 1f;
                    }

                    float[] weights = new float[biomes.Length];
                    for (int k = 0; k < biomes.Length; k++) {
                        weights[k] = biomeWeightsMap[x, y, k];
                    }

                    winningBiomeIndex = GetWinningBiomeIndex(weights);

                } else {
                    // this is pure COLOR BLENDING LOGIC
                    // this part creates transition from beach to land biomes - the outter most gradient on the beach
                    //when we are above the ocean threshold, we exclude the ocean biome from color blending at the land biomes


                    if (continentalnessMap[x, y] < oceanThreshold + outterGradientDelta) {             // this part is for the outside transition area between ocean and beach
                        float supressVal = Mathf.InverseLerp(oceanThreshold + outterGradientDelta, oceanThreshold, continentalnessMap[x, y]);          // get the weight of the beach color for transition from beach to land
                        supressVal = Mathf.Pow(supressVal, 5); // Pow is used to create a more gradual transition on one side of the transition (more gradual towards the land in this case)
                        biomeColorWeights[7] = supressVal;                                 // Pow is used to create a more gradual transition on one side of the transition (more gradual towards the land in this case)
                        for (int k = 0; k < biomes.Length; k++) {
                            if (k != 7) biomeColorWeights[k] *= (1.0f - supressVal);        // suppress all other biomes based on the weight of the beach color
                        }
                    } else {
                        biomeColorWeights[7] = 0f;                          // force ocean weight to 0 for color blending outside coastal strip
                    }

                    // re-normalize weights after zeroing out ocean and adjusting beach weight
                    float sum3 = biomeColorWeights.Sum();
                    for (int k = 0; k < biomes.Length; k++) {
                        biomeColorWeights[k] /= sum3 > 0 ? sum3 : 1f;
                        //biomeWeightsMap[x, y, k] = biomeColorWeights[k];        // update the biomeWeightsMap with the modified weights for color blending
                    }

                    winningBiomeIndex = GetWinningBiomeIndex(biomeColorWeights);

                    // the same code for color assignment depending on biome and height
                    Color blendedColor = Color.black;

                    // cycle trhough all biomes
                    for (int k = 0; k < biomes.Length; k++) {
                        Color biomeColor;
                        if (k == 7) {
                            biomeColor = biomes[7].heightColors[2].color;   // if there is any biome weight (transition part), then just use beach color
                        } else {
                            biomeColor = EvaluateBiomeColorAtHeightWithTransition(biomes[k], blendedHeightMap[x, y], biomeWeightsMap[x, y, k]);
                        }

                        blendedColor += biomeColorWeights[k] * biomeColor;
                    }

                    colorMap[y * mapChunkSize + x] = blendedColor;



                }

                float newCalculatedHeight = blendedHeightMap[x, y];         // tam ko je plaza se drugace izracuna heightMap, da je barva plaze bolj naravna - to vrednost rabimo za mejo za vegetacijo
                blendedHeightMap[x, y] = tempHeight;

                // determine the "winning" biome for each pixel based on the highest weight

                Biome bestBiome = biomes[winningBiomeIndex];
                biomeMap[x, y] = bestBiome;

                // 5. Vegetation Placement Logic: place vegetation based on spacing and density
                int index = 0;
                bool biomeWaterLevel = bestBiome.name == "Grassland" || bestBiome.name == "Temperate Forest" || bestBiome.name == "Taiga" || bestBiome.name == "Rainforest" || bestBiome.name == "Savanna" || bestBiome.name == "Tundra"; // these biomes have water level at the green color (heightColors[1]) - we don't want vegetation to be placed below that level
                if (heightMap[x, y] < bestBiome.heightColors[0].height) continue;        // skip vegetation placement if height is below the green colors (weater or shore)
                if (continentalnessMap[x, y] < oceanThreshold + outterGradientDelta && bestBiome.name != "Ocean") continue;       // skip vegetation placement if we are in the ocean biome and not in the actual beach                            
                if (bestBiome.name == "Ocean" && (continentalnessMap[x, y] >= oceanThreshold || newCalculatedHeight < coastEdgeHeight - coastEdgeHeightRange)) continue; // skip vegetation placement if we are in the ocean biome and not in the actual beach

                //if (bestBiome.name != "Rainforest") continue; // skip vegetation placement if we are not in the rainforest biome (for now, we only place vegetation in the rainforest biome)

                foreach (Vegetation vegType in bestBiome.vegetationTypes) {
                    float spacing = bestBiome.vegSpacing;
                    float density = vegType.density;
                    if (spacing == 0 || density == 0) continue;                         // skip if spacing or density is zero

                    if (x % spacing == 2 * index && y % spacing == 2 * index) {                         // spacing == i zato da na istem mestu ne more biti vec razlicnih vegetacijskih objektov ampak samo en.
                        if ((float)rndm.NextDouble() < density) {
                            vegetationPosition.Add(new Vector2(x, y), vegType);  // add vegetation position to the array (y is 0 for now, we will set it later based on the heightmap)
                        }
                    }

                    index++;
                }


            }
        }

        return new BiomeMapData(colorMap, biomeWeightsMap, blendedHeightMap, biomeMap, vegetationPosition);
    }

    int GetWinningBiomeIndex(float[] biomeWeights) {
        int winningIndex = 0;
        float maxWeight = biomeWeights[0];

        for (int i = 1; i < biomeWeights.Length; i++) {
            if (biomeWeights[i] > maxWeight) {
                maxWeight = biomeWeights[i];
                winningIndex = i;
            }
        }

        return winningIndex;
    }

    // funkcija ki vrne barvo bioma glede na višino (heightValue) in barvne stopnje (heightColors) bioma - z gradientnim prehodom
    Color EvaluateBiomeColorAtHeightWithTransition(Biome biome, float heightValue, float biomeWeight) {
        HeightColor[] stops = biome.heightColors;

        // If there are no height stops defined, return the base biome color
        if (stops == null || stops.Length == 0) {
            return biome.color;
        }

        if (heightValue <= stops[0].height) {
            return stops[0].color;       // if heightValue is less than the first stop, return the first color
        }


        // ce smo na plazi, potem vrni barvo plaze brez prehoda - to odpravi leak modre barve na plazi
        if (biome.name == "Ocean" && heightValue > stops[2].height) {
            return stops[2].color;//new Color(0.0f, 0.8f, 0.2f, 1.0f); //
        }

        float transitionKoeff = mapGen.biomeHeightTransitionWidth; // store the original transition width
        if (biome.name == "Ocean" && heightValue >= stops[1].height && heightValue < stops[2].height) {
            transitionKoeff = 0.1f; // temporarily set transition width to 0.1 for beach transition - transition between beach and ocean color
            // ta zadeva tudi eliminira tiste beach-like gradiente ki so random posejani po celem morju
        }

        // interpolira barvo med dvema višinskima stopnjama glede na višino (heightValue) in koeficient prehoda (transitionKoeff)
        for (int i = 1; i < stops.Length; i++) {
            if (heightValue <= stops[i].height) {
                float t = Mathf.InverseLerp(stops[i - 1].height, stops[i].height, heightValue);
                float transitionWidth = Mathf.Max(0.0001f, transitionKoeff);
                t = Mathf.Pow(t, 1f / transitionWidth);
                return Color.Lerp(stops[i - 1].color, stops[i].color, t);       // ko je biomeHeightTransitionWidth ~ 0.1, potem bo t evaluiral na ~ 0, kar pomeni da bo barva assignana iz prejsnje visinske stopnje - barve bojo shiftane
            }
        }

        return stops[stops.Length - 1].color;       // if heightValue is greater than the last stop, return the last color
    }

    Color EvaluateBiomeColorAtHeightNoTransition(Biome biome, float heightValue) {
        HeightColor[] stops = biome.heightColors;

        // If there are no height stops defined, return the base biome color
        if (stops == null || stops.Length == 0) {
            return biome.color;
        }

        for (int i = 0; i < stops.Length - 1; i++) {
            if (heightValue <= stops[i].height) {
                return stops[i].color;       // return the color of the previous stop without any transition
            }
        }

        return stops[stops.Length - 1].color;       // if heightValue is greater than the last stop, return the last color
    }

}

/// <summary>
/// struct za posamezno vrsto vegetacije (recimo eno drevo, grm, trava) <br/>
/// public GameObject prefab; <br/>
/// public float density; <br/>
/// public float spacing; <br/>
/// </summary>
[System.Serializable]
public struct Vegetation {
    public GameObject prefab;
    public float density;       // how many per unit area
    //public float spacing;     // minimum distance between instances
    public float scale;
    public int count;           // how many instances to place in single position (kako velika je skupina)
    public float groupSpacing;  // ce je count > 1, potem je groupSpacing faktor razdalje med posameznimi instancami
    public bool gpuInstancing;    // whether to use GPU instancing for this vegetation type

}

/// <summary>
/// public string name; <br/>
///public Color color; <br/>
///public float biomeMeshHeightMultiplier; <br/>
///public AnimationCurve biomeMeshHeightCurve; <br/>
///public HeightColor[] heightColors;                   <br/>
///public float idealTemp; <br/>
///public float idealHumidity; <br/>
///public float spread; <br/>
/// </summary>
[System.Serializable]
public struct Biome {
    public string name;
    public Color color;
    public float biomeMeshHeightMultiplier;
    public AnimationCurve biomeMeshHeightCurve;
    public HeightColor[] heightColors;                  // array of height-color pairs to define how the biome's color changes with height

    // biome blending parameters
    public float idealTemp;
    public float idealHumidity;
    public float spread;

    public Vegetation[] vegetationTypes;                // array of vegetation types that can appear in this biome
    public float vegSpacing;                            // how far apart vegetation can be placed

    // public float GetBlendFactor(float temp, float humidity) {
    //     // SIMPLE GAUSSIAN BLEND FUNCTION
    //     // we get a blend factor between 0 and 1 based on how close the given temperature and humidity are to the biome's ideal conditions, using a Gaussian function for smooth blending
    //     /*float tempSpread = 0.04f;
    //     float tempDiff = Mathf.Abs(temp - idealTemp);
    //     float humidityDiff = Mathf.Abs(humidity - idealHumidity);
    //     float distanceSq = tempDiff * tempDiff + humidityDiff * humidityDiff;
    //     return Mathf.Exp(-distanceSq / (2.0f * tempSpread * tempSpread)); */


    //     // GAUSSIAN PLATEAU + SPREAD BLEND FUNCTION
    //     float spread = 0.05f;               // How wide the blending "border" is
    //     float coreRadius = 0.05f;           // How wide the 100% pure "center" is

    //     float tempDiff = temp - idealTemp;
    //     float humidityDiff = humidity - idealHumidity;

    //     // We need actual distance now to subtract the radius accurately
    //     float distance = Mathf.Sqrt(tempDiff * tempDiff + humidityDiff * humidityDiff);

    //     // 1. The "Plateau": If we are inside the core, weight is at absolute maximum
    //     if (distance <= coreRadius) {
    //         return 1.0f;
    //     }

    //     // 2. The "Slope": We only calculate falloff for the distance OUTSIDE the core
    //     float distancePastCore = distance - coreRadius;

    //     // Notice we use distancePastCore instead of distance here
    //     return Mathf.Exp(-(distancePastCore * distancePastCore) / (2.0f * spread * spread));
    // }

    public float GetBlendFactor(float temp, float humidity, float continentalness) {
        // Ocean and Mountains blend on continentalness only, ignore climate
        if (name == "Ocean") {
            return ContinentalBlend(continentalness, 0.0f, 0.06f); // peaks at 0, fades toward 0.35
        }
        if (name == "Mountains") {
            return ContinentalBlend(continentalness, 1.0f, 0.12f);  // peaks at 1, fades toward 0.8
        }

        // All other biomes: suppress weight when continentalness is in ocean/mountain territory
        float continentalSuppress = 1.0f //Mathf.InverseLerp(0.1f, 0.4f, continentalness)  // fade in from ocean side - tega sedaj nocem zato da del obale dobi malo bolj razgiban teren ce je kaksen visji biome blizu
                                * Mathf.InverseLerp(0.9f, 0.75f, continentalness);          // fade out toward mountain side
                                                                                            //float continentalSuppress = 1f;

        // if (continentalness > 0.8f || continentalness < 0.35f) {
        //     continentalSuppress = 0f; // fully suppress if we're in the "forbidden" continentalness zones
        // }

        float spread = 0.05f;
        float coreRadius = 0.05f;

        float tempDiff = temp - idealTemp;
        float humidityDiff = humidity - idealHumidity;
        float distance = Mathf.Sqrt(tempDiff * tempDiff + humidityDiff * humidityDiff);

        float climateWeight;
        if (distance <= coreRadius) {
            climateWeight = 1.0f;
        } else {
            float distancePastCore = distance - coreRadius;
            climateWeight = Mathf.Exp(-(distancePastCore * distancePastCore) / (2.0f * spread * spread));
        }

        return climateWeight * continentalSuppress;
    }

    // funkcija vrne weight za ocean in mountains glede na continentalness (oceani in gore imajo svoj weight samo glede na continentalness, ne glede na temp in humidity)
    private float ContinentalBlend(float continentalness, float peak, float df) {
        //if (edge - continentalness + peak < 0) return 0f;   // returnamo 0 ko je continentalness vecji kot 0.35 - zato da ocean ne prodira proti celini
        float spread = 0.12f;
        float coreRadius = 0.01f;
        float distance = Mathf.Abs(continentalness - peak);

        if (distance <= coreRadius) return 1.0f;

        float distancePastCore = distance - coreRadius;
        return Mathf.Exp(-(distancePastCore * distancePastCore) / (2.0f * spread * spread));
    }
}
